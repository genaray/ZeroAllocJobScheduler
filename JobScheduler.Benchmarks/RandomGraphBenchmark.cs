using Schedulers.Benchmarks.Utils.Graph;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Schedulers.Benchmarks;


/// <summary>
/// Benchmark adding a ton of jobs to the queue, flushing, and then completing them.
/// </summary>
[MemoryDiagnoser]
public class RandomGraphBenchmark
{
    /*
    private JobScheduler Scheduler = null!;

    /// <summary>
    /// The thread count tested
    /// </summary>
    [Params(0)] public int Threads;

    /// <summary>
    /// The maximum amount of concurrent jobs active at one time, and nodes in the graph.
    /// </summary>
    [Params(32, 128, 256)] public int ConcurrentJobs;

    /// <summary>
    /// How many times to evaluate the (same) graph in a trial
    /// </summary>
    [Params(512)] public int Waves;

    /// <summary>
    /// The maximum Degree of the random graph (how interconnected the nodes are).
    /// Not all nodes will have this degree, but most will, assuming the EdgeRate is large enough.
    /// </summary>
    [Params(4)] public int Degree;

    /// <summary>
    /// How frequently an edge is generated between a potential parent and potential child.
    /// Limited by <see cref="Degree"/>, so if <see cref="EdgeChance"/> is high and <see cref="Degree"/> is low
    /// the degree will almost always be reached.
    /// </summary>
    [Params(0.05f, 0.2f)] public float EdgeChance;

    JobHandle[] Handles = null!;
    DirectedAcyclicGraph Graph = null!;
    List<DirectedAcyclicGraph.Node> OrderedNodes = null!;

    private class EmptyJob : IJob
    {
        public void Execute() { }
    }

    private readonly static EmptyJob Empty = new();

    [IterationSetup]
    public void Setup()
    {
        var config = new JobScheduler.Config
        {
            // * 2 because we actually duplicate the job whenever we schedule a dependency handle
            // we should consider finding a way to CombineDependencies() without scheduling a whole extra interstitial job
            MaxExpectedConcurrentJobs = ConcurrentJobs * 2,
            StrictAllocationMode = true,
            ThreadPrefixName = nameof(ManyJobsBenchmark),
            ThreadCount = Threads
        };
        Scheduler = new(config);
        Handles = new JobHandle[ConcurrentJobs];
        OrderedNodes = new(ConcurrentJobs);

        var minJobsPerRank = Math.Sqrt(ConcurrentJobs);
        var maxJobsPerRank = Math.Sqrt(ConcurrentJobs) + 5;

        Graph = GraphGenerator.GenerateRandomGraph(new()
        {
            EdgeChance = EdgeChance,
            MaxDegree = Degree,
            Nodes = ConcurrentJobs,
            NodesPerRank = new((int)minJobsPerRank, (int)maxJobsPerRank),
            Seed = null
        });

        // add all the cached arrays for use in CombinedDependencies
        void CollectNodes(DirectedAcyclicGraph.Node node)
        {
            if (!OrderedNodes.Contains(node)) OrderedNodes.Add(node);
            foreach (var child in node.Children)
            {
                CollectNodes(child);
            }
        }

        CollectNodes(Graph.RootNode);
        // we process the nodes in increasing numerical order always
        // that way we ensure we schedule parents before children
        OrderedNodes = OrderedNodes.OrderBy(node => node.ID).ToList();

        foreach (var node in OrderedNodes)
        {
            node.Data ??= new JobHandle[node.Parents.Count];
        }
    }

    [IterationCleanup]
    public void Cleanup()
    {
        Scheduler.Dispose();
    }

    [Benchmark]
    public void BenchmarkGraph()
    {
        for (int w = 0; w < Waves; w++)
        {
            foreach (var node in OrderedNodes)
            {
                var array = (JobHandle[])node.Data!;
                for (int i = 0; i < array.Length; i++)
                {
                    array[i] = Handles[node.Parents[i].ID];
                }

                // here's where the duplication occurs; two schedules!
                Handles[node.ID] = Scheduler.Schedule(Empty, Scheduler.CombineDependencies(array));
            }
            Scheduler.Flush();
            foreach (var handle in Handles)
            {
                handle.Complete();
            }
        }
    }*/
}
