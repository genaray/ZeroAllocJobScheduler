using System.Diagnostics.CodeAnalysis;
using BenchmarkDotNet.Jobs;
using JobScheduler.Benchmarks.Utils.Job;
using JobScheduler.Test.Utils;
using JobScheduler.Test.Utils.CustomConstraints;

namespace JobScheduler.Test;

[SuppressMessage("Assertion", "NUnit2045:Use Assert.Multiple",
    Justification = "Multiple asserts are not appropriate as later code")]
[SuppressMessage("Style", "IDE0053:Use expression body for lambda expression",
    Justification = "Lambda block bodies are necessary for NUnit to recognize the lambda as a TestDelegate.")]
[SuppressMessage("Style", "IDE0200:Remove unnecessary lambda expression",
    Justification = "Lambda block bodies are necessary for NUnit to recognize the lambda as a TestDelegate.")]
[SuppressMessage("Style", "IDE0002:Name can be simplified",
    Justification = "While NUnit.Framework.Is.Not is lexically simpler, it is stylistically more complex.")]
internal class ParallelJobTests : SchedulerTestFixture
{
    protected override bool StrictAllocationMode
    {
        get => false;
    }
    protected override int MaxExpectedConcurrentJobs
    {
        get => (ThreadCount == 0 ? Environment.ProcessorCount : ThreadCount) * 64;
    }
    public ParallelJobTests(int threads) : base(threads) { }

    [Test]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(4)]
    [TestCase(8)]
    [TestCase(16)]
    [TestCase(32)]
    [TestCase(64)]
    [TestCase(1024 * 1024)]
    public void ParallelJobCompletes(int size)
    {
        var job = new ParallelTestJob(ThreadCount, size);
        JobHandle handle = default;
        Assert.That(() => { handle = Scheduler.Schedule(job, size); }, Is.Not.AllocatingMemory());
        Assert.That(job.IsTotallyIncomplete, Is.True);
        Assert.That(() => { Scheduler.Flush(); }, Is.Not.AllocatingMemory());
        Assert.That(() => { handle.Complete(); }, Is.Not.AllocatingMemory());
        Assert.That(job.IsTotallyComplete, Is.True);
    }

    [Test]
    [TestCase(1)]
    [TestCase(64)]
    [TestCase(1024 * 1024)]
    public void ParallelJobDependenciesComplete(int size)
    {
        var job1 = new SleepJob(100);
        var job2 = new SleepJob(100);
        var job3 = new SleepJob(100);
        var parallelJob = new ParallelTestJob(ThreadCount, size);

        var h1 = Scheduler.Schedule(job1);
        var h2 = Scheduler.Schedule(job2);
        var h3 = Scheduler.Schedule(job3, h2);
        var c = Scheduler.CombineDependencies(new JobHandle[] { h1, h3 });
        var hp = Scheduler.Schedule(parallelJob, size, c);

        Scheduler.Flush();
        Thread.Sleep(5);
        Assert.That(job1.Result, Is.EqualTo(0));
        Assert.That(job2.Result, Is.EqualTo(0));
        Assert.That(job3.Result, Is.EqualTo(0));
        Assert.That(parallelJob.IsTotallyIncomplete, Is.True);

        c.Complete();
        Assert.That(job1.Result, Is.EqualTo(1));
        Assert.That(job2.Result, Is.EqualTo(1));
        Assert.That(job3.Result, Is.EqualTo(1));

        hp.Complete();
        Assert.That(parallelJob.IsTotallyComplete, Is.True);
        Assert.That(job1.Result, Is.EqualTo(1));
        Assert.That(job2.Result, Is.EqualTo(1));
        Assert.That(job3.Result, Is.EqualTo(1));
    }

    [Test]
    [TestCase(1)]
    [TestCase(64)]
    [TestCase(1024 * 1024)]
    public void ParallelJobDependentsComplete(int size)
    {
        var job1 = new SleepJob(100);
        var job2 = new SleepJob(100);
        var job3 = new SleepJob(100);
        var parallelJob = new ParallelTestJob(ThreadCount, size);

        var hp = Scheduler.Schedule(parallelJob, size);
        var h1 = Scheduler.Schedule(job1, hp);
        var h2 = Scheduler.Schedule(job2, h1);
        var h3 = Scheduler.Schedule(job3, hp);

        Scheduler.Flush();
        Thread.Sleep(50);
        // we've had enough time to complete the parallel job
        Assert.That(parallelJob.IsTotallyComplete, Is.True);
        // but not the others
        Assert.That(job1.Result, Is.EqualTo(0));
        Assert.That(job2.Result, Is.EqualTo(0));
        Assert.That(job3.Result, Is.EqualTo(0));

        h2.Complete();
        h3.Complete();
        Assert.That(job1.Result, Is.EqualTo(1));
        Assert.That(job2.Result, Is.EqualTo(1));
        Assert.That(job3.Result, Is.EqualTo(1));
        Assert.That(parallelJob.IsTotallyComplete, Is.True);
    }

    [Test]
    [TestCase(64, 2048, true)]
    [TestCase(64, 2048, false)]
    public void ManyParallelJobsComplete(int count, int size, bool flushAfterEverySchedule)
    {
        var jobs = new List<(ParallelTestJob, JobHandle)>(count);
        for (var i = 0; i < count; i++)
        {
            var job = new ParallelTestJob(ThreadCount, size);
            Assert.That(() => { jobs.Add((job, Scheduler.Schedule(job, size)));  }, Is.Not.AllocatingMemory());
            if (flushAfterEverySchedule)
            {
                Assert.That(() => { Scheduler.Flush(); }, Is.Not.AllocatingMemory());
            }
        }

        if (!flushAfterEverySchedule)
        {
            Assert.That(() => { Scheduler.Flush(); }, Is.Not.AllocatingMemory());
        }

        foreach (var (job, handle) in jobs)
        {
            Assert.That(() => { handle.Complete(); }, Is.Not.AllocatingMemory());
            Assert.That(job.IsTotallyComplete, Is.True);
        }

        // double check
        foreach (var (job, _) in jobs)
        {
            Assert.That(job.IsTotallyComplete, Is.True);
        }
    }

    [Test]
    [TestCase(64, 2048, true, true)]
    [TestCase(64, 2048, false, true)]
    [TestCase(64, 2048, true, false)]
    [TestCase(64, 2048, false, false)]
    public void ManyDependentParallelJobsComplete(int count, int size, bool reverse, bool flushAfterEverySchedule)
    {
        var jobs = new List<(ParallelTestJob, JobHandle)>(count);
        for (var i = 0; i < count; i++)
        {
            var job = new ParallelTestJob(ThreadCount, size);            
            Assert.That(() => { jobs.Add((job, Scheduler.Schedule(job, size, i != 0 ? jobs[i - 1].Item2 : null))); }, Is.Not.AllocatingMemory());
            if (flushAfterEverySchedule)
            {
                Assert.That(() => { Scheduler.Flush(); }, Is.Not.AllocatingMemory());
            }
        }

        if (!flushAfterEverySchedule)
        {
            Assert.That(() => { Scheduler.Flush(); }, Is.Not.AllocatingMemory());
        }

        if (reverse)
        {
            jobs.Reverse();
        }

        for (var i = 0; i < count; i++)
        {
            var (job, handle) = jobs[i];
            // in the reversed case, only do one Complete() call
            if (i == 0 && reverse)
            {
                Assert.That(() => { handle.Complete(); }, Is.Not.AllocatingMemory());
            }
            else if (!reverse)
            {
                Assert.That(() => { handle.Complete(); }, Is.Not.AllocatingMemory());
            }

            Assert.That(job.IsTotallyComplete, Is.True);
        }

        // double check
        foreach (var (job, _) in jobs)
        {
            Assert.That(job.IsTotallyComplete, Is.True);
        } 
    }

    [Test]
    [TestCase(5, 64, 250, 512)]
    public void ParallelJobGraphCompletes(int graphCount, int nodesPerGraph, int waves, int size)
    {
        Dictionary<int, ParallelTestJob> jobs = new();

        GraphRunner.TestGraph(graphCount, nodesPerGraph, waves,
            (index, dependency) =>
            {
                var job = new ParallelTestJob(ThreadCount, size);
                jobs[index] = job;
                return Scheduler.Schedule(job, size, dependency);
            },
            Scheduler,
            (index) =>
            {
                Assert.That(jobs[index].IsTotallyComplete, Is.True);
            });
    }

    private class ParallelSleepJob : IJobParallelFor
    {
        private readonly int _sleep;

        public ParallelSleepJob(int expectedSize, int sleep)
        {
            ThreadIDs = new int[expectedSize];
            _sleep = sleep;
        }
        public int[] ThreadIDs { get; }

        public int ThreadCount { get => 0; }

        public void Execute(int index)
        {
            ThreadIDs[index] = Environment.CurrentManagedThreadId;
            if (_sleep > 0)
            {
                Thread.Sleep(_sleep);
            }
        }
    }

    [TestCase(1024 * 1024, 0)]
    [TestCase(256, 1)]
    public void ParallelJobRunsParallel(int size, int sleep)
    {
        var job = new ParallelSleepJob(size, sleep);
        var handle = Scheduler.Schedule(job, size);
        Scheduler.Flush();
        handle.Complete();

        Assert.That(job.ThreadIDs.Distinct(), Has.Exactly(Scheduler.ThreadCount).Items);
    }
}

