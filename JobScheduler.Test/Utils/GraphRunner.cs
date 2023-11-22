using Schedulers.Benchmarks.Utils.Graph;

namespace Schedulers.Test.Utils;
internal static class GraphRunner
{
    /// <summary>
    /// Runs a graph test.
    /// </summary>
    /// <param name="graphCount">How many random graph trials to run</param>
    /// <param name="nodesPerGraph">How many nodes in each graph</param>
    /// <param name="waves">How many time to run each randomly generated graph through the scheduler</param>
    /// <param name="scheduleFunc">
    ///     Given a <see cref="JobHandle"/> dependency and node index, schedules a job of the caller's choice and returns
    ///     the resulting <see cref="JobHandle"/>
    /// </param>
    /// <param name="scheduler">The job scheduler to use</param>
    /// <param name="validateNode">An optional function to run post processing once a node must be complete.</param>
    public static void TestGraph(int graphCount, int nodesPerGraph, int waves,
        Func<int, JobHandle, JobHandle> scheduleFunc,
        JobScheduler scheduler,
        Action<int>? validateNode = null)
    {
        var handles = new JobHandle[nodesPerGraph];
        var orderedNodes = new List<DirectedAcyclicGraph.Node>(nodesPerGraph);

        for (var g = 0; g < graphCount; g++)
        {
            orderedNodes.Clear();

            var minJobsPerRank = Math.Sqrt(nodesPerGraph);
            var maxJobsPerRank = Math.Sqrt(nodesPerGraph) + 5;

            var graph = GraphGenerator.GenerateRandomGraph(new()
            {
                EdgeChance = 0.1f,
                MaxDegree = 4,
                Nodes = nodesPerGraph,
                NodesPerRank = new((int)minJobsPerRank, (int)maxJobsPerRank),
                Seed = null
            });

            // add all the cached arrays for use in CombinedDependencies
            void collectNodes(DirectedAcyclicGraph.Node node)
            {
                if (!orderedNodes.Contains(node))
                {
                    orderedNodes.Add(node);
                }

                foreach (var child in node.Children)
                {
                    collectNodes(child);
                }
            }

            collectNodes(graph.RootNode);
            // we process the nodes in increasing numerical order always
            // that way we ensure we schedule parents before children
            orderedNodes = orderedNodes.OrderBy(node => node.ID).ToList();

            foreach (var node in orderedNodes)
            {
                node.Data ??= new JobHandle[node.Parents.Count];
            }

            // actually execute
            for (var w = 0; w < waves; w++)
            {
                foreach (var node in orderedNodes)
                {
                    var array = (JobHandle[])node.Data!;
                    for (var i = 0; i < array.Length; i++)
                    {
                        array[i] = handles[node.Parents[i].ID];
                    }

                    // here's where the duplication occurs; two schedules!
                    handles[node.ID] = scheduleFunc.Invoke(node.ID, scheduler.CombineDependencies(array));
                }

                scheduler.Flush();
                foreach (var node in orderedNodes)
                {
                    handles[node.ID].Complete();
                    validateNode?.Invoke(node.ID);
                }
            }
        }
    }
}
