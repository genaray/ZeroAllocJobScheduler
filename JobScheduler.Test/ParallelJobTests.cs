using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using BenchmarkDotNet.Jobs;
using Schedulers.Test.Utils;
using Schedulers.Test.Utils.CustomConstraints;

namespace Schedulers.Test;

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
    [TestCase(1, 1)]
    [TestCase(2, 1)]
    [TestCase(4, 1)]
    [TestCase(8, 1)]
    [TestCase(16, 1)]
    [TestCase(32, 1)]
    [TestCase(32, 3)]
    [TestCase(32, 5)]
    [TestCase(64, 1)]
    [TestCase(1024 * 1024, 1)]
    [TestCase(1024 * 1024, 2)]
    [TestCase(1024 * 1024, 3)]
    [TestCase(1024 * 1024, 5)]
    [TestCase(1024 * 1024, 7)]
    [TestCase(1024 * 1024, 16)]
    [TestCase(1024 * 1024, 512)]
    public void ParallelJobCompletes(int size, int batchSize)
    {
        //var job = new ParallelTestJob(batchSize, ThreadCount, size);
        //JobHandle handle = default;
        //Assert.That(() => { handle = Scheduler.Schedule(job, size); }, Is.Not.AllocatingMemory());
        //job.AssertIsTotallyIncomplete();
        //Assert.That(() => { Scheduler.Flush(); }, Is.Not.AllocatingMemory());
        //Assert.That(() => { handle.Complete(); }, Is.Not.AllocatingMemory());
        //job.AssertIsTotallyComplete();

        var job = new ParallelTestJob(batchSize, ThreadCount, size);
        JobHandle handle = default;
        handle = Scheduler.Schedule(job, size);
        job.AssertIsTotallyIncomplete();
        Scheduler.Flush();
        handle.Complete();
        job.AssertIsTotallyComplete();
    }

    [Test]
    [TestCase(1, 1)]
    [TestCase(64, 4)]
    [TestCase(1024 * 1024, 64)]
    public void ParallelJobDependenciesComplete(int size, int batchSize)
    {
        var job1 = new SleepJob(100);
        var job2 = new SleepJob(100);
        var job3 = new SleepJob(100);
        var parallelJob = new ParallelTestJob(batchSize, ThreadCount, size);

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
        parallelJob.AssertIsTotallyIncomplete();

        c.Complete();
        Assert.That(job1.Result, Is.EqualTo(1));
        Assert.That(job2.Result, Is.EqualTo(1));
        Assert.That(job3.Result, Is.EqualTo(1));

        hp.Complete();
        parallelJob.AssertIsTotallyComplete();
        Assert.That(job1.Result, Is.EqualTo(1));
        Assert.That(job2.Result, Is.EqualTo(1));
        Assert.That(job3.Result, Is.EqualTo(1));
    }

    [Test]
    [TestCase(1, 1)]
    [TestCase(64, 4)]
    [TestCase(1024 * 1024, 64)]
    public void ParallelJobDependentsComplete(int size, int batchSize)
    {
        var job1 = new SleepJob(100);
        var job2 = new SleepJob(100);
        var job3 = new SleepJob(100);
        var parallelJob = new ParallelTestJob(batchSize, ThreadCount, size);

        var hp = Scheduler.Schedule(parallelJob, size);
        var h1 = Scheduler.Schedule(job1, hp);
        var h2 = Scheduler.Schedule(job2, h1);
        var h3 = Scheduler.Schedule(job3, hp);

        Scheduler.Flush();
        Thread.Sleep(50);
        // we've had enough time to complete the parallel job
        parallelJob.AssertIsTotallyComplete();
        // but not the others
        Assert.That(job1.Result, Is.EqualTo(0));
        Assert.That(job2.Result, Is.EqualTo(0));
        Assert.That(job3.Result, Is.EqualTo(0));

        h2.Complete();
        h3.Complete();
        Assert.That(job1.Result, Is.EqualTo(1));
        Assert.That(job2.Result, Is.EqualTo(1));
        Assert.That(job3.Result, Is.EqualTo(1));
        parallelJob.AssertIsTotallyComplete();
    }

    [Test]
    [TestCase(64, 2048, 16, true)]
    [TestCase(64, 2048, 16, false)]
    public void ManyParallelJobsComplete(int count, int size, int batchSize, bool flushAfterEverySchedule)
    {
        var jobs = new List<(ParallelTestJob, JobHandle)>(count);
        for (var i = 0; i < count; i++)
        {
            var job = new ParallelTestJob(batchSize, ThreadCount, size);
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
            job.AssertIsTotallyComplete();
        }

        // double check
        foreach (var (job, _) in jobs)
        {
            job.AssertIsTotallyComplete();
        }
    }

    [Test]
    [TestCase(64, 2048, 16, true, true)]
    [TestCase(64, 2048, 16, false, true)]
    [TestCase(64, 2048, 16, true, false)]
    [TestCase(64, 2048, 16, false, false)]
    public void ManyDependentParallelJobsComplete(int count, int size, int batchSize, bool reverse,
        bool flushAfterEverySchedule)
    {
        var jobs = new List<(ParallelTestJob, JobHandle)>(count);
        for (var i = 0; i < count; i++)
        {
            var job = new ParallelTestJob(batchSize, ThreadCount, size);            
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

            job.AssertIsTotallyComplete();
        }

        // double check
        foreach (var (job, _) in jobs)
        {
            job.AssertIsTotallyComplete();
        } 
    }

    [Test]
    [TestCase(5, 64, 250, 512, 3)]
    public void ParallelJobGraphCompletes(int graphCount, int nodesPerGraph, int waves, int size, int batchSize)
    {
        Dictionary<int, ParallelTestJob> jobs = new();

        GraphRunner.TestGraph(graphCount, nodesPerGraph, waves,
            (index, dependency) =>
            {
                var job = new ParallelTestJob(batchSize, ThreadCount, size);
                jobs[index] = job;
                return Scheduler.Schedule(job, size, dependency);
            },
            Scheduler,
            (index) =>
            {
                jobs[index].AssertIsTotallyComplete();
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
        // keep a low batch size if we sleep (lots of work simulated)
        public int BatchSize { get => _sleep > 0 ? 32 : 1; }

        public void Execute(int index)
        {
            ThreadIDs[index] = Environment.CurrentManagedThreadId;
            if (_sleep > 0)
            {
                Thread.Sleep(_sleep);
            }
        }

        public void Finish()
        {
            Assert.That(ThreadIDs, Does.Not.Contain(0));
        }
    }

    [TestCase(1024 * 1024, 0)]
    [TestCase(512, 1)]
    public void ParallelJobRunsParallel(int size, int sleep)
    {
        var job = new ParallelSleepJob(size, sleep);
        var handle = Scheduler.Schedule(job, size);
        Scheduler.Flush();
        handle.Complete();

        Assert.That(job.ThreadIDs.Distinct(), Has.Exactly(Scheduler.ThreadCount).Items);
    }
}

