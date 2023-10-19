using JobScheduler.Benchmarks.Utils.Graph;
using JobScheduler.Test.Utils;

namespace JobScheduler.Test;
[TestFixture(0, 32)]
[TestFixture(1, 32)]
[TestFixture(2, 32)]
[TestFixture(4, 32)]
[TestFixture(8, 32)]
[TestFixture(16, 32)]
[TestFixture(0, 2048)]
[TestFixture(1, 2048)]
[TestFixture(2, 2048)]
[TestFixture(4, 2048)]
[TestFixture(8, 2048)]
[TestFixture(16, 2048)]
internal class StressTests : SchedulerTestFixture
{
    public StressTests(int threads, int maxJobs) : base(threads)
    {
        MaxExpectedConcurrentJobs = maxJobs;
    }

    protected override bool StrictAllocationMode => false;

    protected override int MaxExpectedConcurrentJobs { get; }

    [Test]
    [TestCase(1000, 10, true, false)]
    [TestCase(10, 1000, false, true)]
    [TestCase(1000, 10, true, false)]
    [TestCase(10, 1000, false, true)]
    public void StressTestJobs(int jobCount, int waveCount, bool useDependenciesOnWaves, bool useDependenciesOnJobs)
    {
        List<TestJob> jobs = new();
        for (int j = 0; j < jobCount; j++)
        {
            jobs.Add(new TestJob());
        }
        for (int w = 0; w < waveCount; w++)
        {
            List<JobHandle> handles = new();
            JobHandle? lastWaveHandle = null;
            foreach (var job in jobs)
            {
                if (!handles.Any())
                {
                    // setup the first one to use the last wave, if necessary
                    if (useDependenciesOnWaves && lastWaveHandle is not null)
                        handles.Add(Scheduler.Schedule(job, lastWaveHandle));
                    else handles.Add(Scheduler.Schedule(job));
                }

                // depend on the previous job
                else if (useDependenciesOnJobs)
                    handles.Add(Scheduler.Schedule(job, handles.Last()));

                // just do a normal schedule if none of those
                else handles.Add(Scheduler.Schedule(job));
            }

            if (useDependenciesOnWaves)
            {
                lastWaveHandle = Scheduler.CombineDependencies(handles.ToArray());
                Scheduler.Flush();
                lastWaveHandle.Value.Complete();
            }
            else
            {
                Scheduler.Flush();
                JobHandle.CompleteAll(handles);
            }
            foreach (var job in jobs)
            {
                Assert.That(job.Result, Is.EqualTo(w + 1));
            }
        }
    }

    class EmptyJob : IJob
    {
        public void Execute() { }
    }

    [Test]
    [TestCase(5, 128, 500)]
    public void StressTestGraph(int graphCount, int nodesPerGraph, int waves)
    {
        var handles = new JobHandle[nodesPerGraph];
        var orderedNodes = new List<DirectedAcyclicGraph.Node>(nodesPerGraph);
        var emptyJob = new EmptyJob();
        for (int g = 0; g < graphCount; g++)
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
            void CollectNodes(DirectedAcyclicGraph.Node node)
            {
                if (!orderedNodes.Contains(node)) orderedNodes.Add(node);
                foreach (var child in node.Children)
                {
                    CollectNodes(child);
                }
            }

            CollectNodes(graph.RootNode);
            // we process the nodes in increasing numerical order always
            // that way we ensure we schedule parents before children
            orderedNodes = orderedNodes.OrderBy(node => node.ID).ToList();

            foreach (var node in orderedNodes)
            {
                node.Data ??= new JobHandle[node.Parents.Count];
            }


            // actually execute
            for (int w = 0; w < waves; w++)
            {
                foreach (var node in orderedNodes)
                {
                    var array = (JobHandle[])node.Data!;
                    for (int i = 0; i < array.Length; i++)
                    {
                        array[i] = handles[node.Parents[i].ID];
                    }

                    // here's where the duplication occurs; two schedules!
                    handles[node.ID] = Scheduler.Schedule(emptyJob, Scheduler.CombineDependencies(array));
                }
                Scheduler.Flush();
                foreach (var handle in handles)
                {
                    handle.Complete();
                }
            }
        }
    }
}
