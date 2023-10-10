using JobScheduler.Test.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JobScheduler.Test;

internal class StressTests : SchedulerTestFixture
{
    public StressTests(int threads) : base(threads) { }

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
}
