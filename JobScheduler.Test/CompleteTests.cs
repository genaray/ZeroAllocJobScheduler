using JobScheduler.Test.Utils;

namespace JobScheduler.Test;

internal class CompleteTests : SchedulerTestFixture
{
    public CompleteTests(int threads) : base(threads) { }

    [Test]
    public void OneJobCompletes()
    {
        var job = new SleepJob(10);
        Assert.That(job.Result, Is.EqualTo(0));

        var handle = Scheduler.Schedule(job);

        Scheduler.Flush();
        handle.Complete();

        Assert.That(job.Result, Is.EqualTo(1));
    }

    [Test]
    public void TwoJobsComplete()
    {
        var job1 = new SleepJob(10);
        var job2 = new SleepJob(10);

        Assert.Multiple(() =>
        {
            Assert.That(job1.Result, Is.EqualTo(0));
            Assert.That(job2.Result, Is.EqualTo(0));
        });

        var handle1 = Scheduler.Schedule(job1);
        var handle2 = Scheduler.Schedule(job2);

        Scheduler.Flush();

        handle1.Complete();
        handle2.Complete();

        Assert.Multiple(() =>
        {
            Assert.That(job1.Result, Is.EqualTo(1));
            Assert.That(job2.Result, Is.EqualTo(1));
        });
    }

    [Test]
    public void TwoSeparateJobsComplete()
    {
        var job1 = new SleepJob(10);
        var job2 = new SleepJob(10);

        Assert.Multiple(() =>
        {
            Assert.That(job1.Result, Is.EqualTo(0));
            Assert.That(job2.Result, Is.EqualTo(0));
        });

        var handle1 = Scheduler.Schedule(job1);
        Scheduler.Flush();
        handle1.Complete();

        var handle2 = Scheduler.Schedule(job2);
        Scheduler.Flush();
        handle2.Complete();

        Assert.Multiple(() =>
        {
            Assert.That(job1.Result, Is.EqualTo(1));
            Assert.That(job2.Result, Is.EqualTo(1));
        });
    }

    // these parameters are in mind of 1 core, the lowest overall core count test. so each test case will delay a minimum of sleepTime * jobCount ms.
    [Test]
    [TestCase(0, 1000)]
    [TestCase(5, 20)]
    [TestCase(25, 4)]
    [TestCase(50, 4)] // this should have a runtime of less than 200ms to ensure we're completing in parallel and not sequentially; not sure how to nunit test that
    public void ManyJobsComplete(int sleepTime, int jobCount)
    {
        var jobs = Enumerable.Repeat(0, jobCount).Select(_ => new SleepJob(sleepTime)).ToList();
        var handles = jobs.Select(j => Scheduler.Schedule(j)).ToList();

        Scheduler.Flush();

        foreach (var handle in handles) handle.Complete();

        CollectionAssert.AreEqual(jobs.Select(job => job.Result), Enumerable.Repeat(1, jobCount).ToList());
    }

    [Test]
    public void DelayedMainThreadCompletesJob()
    {
        var job = new SleepJob(0);
        var handle = Scheduler.Schedule(job);
        Scheduler.Flush();
        Thread.Sleep(10);
        handle.Complete();
    }

    // TODO: test multi-thread Complete; ensure that none hang
}