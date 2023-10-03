using JobScheduler.Extensions;
using JobScheduler.Test.Utils;

namespace JobScheduler.Test;

internal class CompleteTests : SchedulerTestFixture
{
    [Test]
    public void OneJobCompletes()
    {
        var job = new SleepJob(25);
        Assert.That(job.Result, Is.EqualTo(0));

        var handle = job.Schedule(false);

        Scheduler.Flush();
        handle.Complete();
        handle.Return();

        Assert.That(job.Result, Is.EqualTo(1));
    }

    [Test]
    public void TwoJobsComplete()
    {
        var job1 = new SleepJob(25);
        var job2 = new SleepJob(25);

        Assert.Multiple(() =>
        {
            Assert.That(job1.Result, Is.EqualTo(0));
            Assert.That(job2.Result, Is.EqualTo(0));
        });

        var handle1 = job1.Schedule(false);
        var handle2 = job2.Schedule(false);

        Scheduler.Flush();

        handle1.Complete();
        handle2.Complete();

        handle1.Return();
        handle2.Return();

        Assert.Multiple(() =>
        {
            Assert.That(job1.Result, Is.EqualTo(1));
            Assert.That(job2.Result, Is.EqualTo(1));
        });
    }

    [Test]
    public void TwoSeparateJobsComplete()
    {
        var job1 = new SleepJob(25);
        var job2 = new SleepJob(25);

        Assert.Multiple(() =>
        {
            Assert.That(job1.Result, Is.EqualTo(0));
            Assert.That(job2.Result, Is.EqualTo(0));
        });

        var handle1 = job1.Schedule(false);
        Scheduler.Flush();
        handle1.Complete();

        var handle2 = job2.Schedule(false);
        Scheduler.Flush();
        handle2.Complete();

        handle1.Return();
        handle2.Return();

        Assert.Multiple(() =>
        {
            Assert.That(job1.Result, Is.EqualTo(1));
            Assert.That(job2.Result, Is.EqualTo(1));
        });
    }

    [Test]
    [TestCase(0, 1000)]
    [TestCase(5, 200)]
    [TestCase(25, 40)]
    [TestCase(50, 4)] // this should have a runtime of less than 200ms to ensure we're completing in parallel and not sequentially; not sure how to nunit test that
    public void ManyJobsComplete(int sleepTime, int jobCount)
    {
        var jobs = Enumerable.Repeat(0, jobCount).Select(_ => new SleepJob(sleepTime)).ToList();
        var handles = jobs.Select(j => j.Schedule(false)).ToList();

        Scheduler.Flush();

        foreach (var handle in handles) handle.Complete();

        foreach (var handle in handles) handle.Return();

        CollectionAssert.AreEqual(jobs.Select(job => job.Result), Enumerable.Repeat(1, jobCount).ToList());
    }
}