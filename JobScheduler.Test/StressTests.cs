using Schedulers.Test.Utils;

namespace Schedulers.Test;
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

    protected override bool StrictAllocationMode
    {
        get => false;
    }

    protected override int MaxExpectedConcurrentJobs { get; }

    [Test]
    [TestCase(1000, 10, true, false)]
    [TestCase(10, 1000, false, true)]
    [TestCase(1000, 10, true, false)]
    [TestCase(10, 1000, false, true)]
    public void StressTestJobs(int jobCount, int waveCount, bool useDependenciesOnWaves, bool useDependenciesOnJobs)
    {
        List<TestJob> jobs = new();
        for (var j = 0; j < jobCount; j++)
        {
            jobs.Add(new TestJob());
        }

        for (var w = 0; w < waveCount; w++)
        {
            List<JobHandle> handles = new();
            JobHandle? lastWaveHandle = null;
            foreach (var job in jobs)
            {
                if (!handles.Any())
                {
                    // setup the first one to use the last wave, if necessary
                    if (useDependenciesOnWaves && lastWaveHandle is not null)
                    {
                        handles.Add(Scheduler.Schedule(job, lastWaveHandle));
                    }
                    else
                    {
                        handles.Add(Scheduler.Schedule(job));
                    }
                }

                // depend on the previous job
                else if (useDependenciesOnJobs)
                {
                    handles.Add(Scheduler.Schedule(job, handles.Last()));
                }

                // just do a normal schedule if none of those
                else
                {
                    handles.Add(Scheduler.Schedule(job));
                }
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

    private class EmptyJob : IJob
    {
        public void Execute() { }
    }

    [Test]
    [TestCase(5, 128, 500)]
    public void StressTestGraph(int graphCount, int nodesPerGraph, int waves)
    {
        var empty = new EmptyJob();
        GraphRunner.TestGraph(graphCount, nodesPerGraph, waves, (_, dependency) =>
        {
            return Scheduler.Schedule(empty, dependency);
        }, Scheduler);
    }
}
