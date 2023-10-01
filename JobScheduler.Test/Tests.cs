using JobScheduler.Extensions;
using System.Reflection.Metadata;

namespace JobScheduler.Test;

public class SleepJob : IJob
{
    public SleepJob(int time)
    {
        _time = time;
    }
    private readonly int _time;
    public int Result { get; private set; }
    public void Execute()
    {
        Thread.Sleep(_time);
        Result = 1;
    }
}

public class CompleteTests
{
    private JobScheduler _jobScheduler = null!;

    [SetUp]
    public void Setup()
    {
        _jobScheduler = new JobScheduler("Test");
    }

    [TearDown]
    public void Clean()
    {
        _jobScheduler.Dispose();
    }

    [Test]
    public void OneJobCompletes()
    {
        var job = new SleepJob(25);
        Assert.That(job.Result, Is.EqualTo(0));

        var handle = job.Schedule(false);

        _jobScheduler.Flush();
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

        _jobScheduler.Flush();

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
        _jobScheduler.Flush();
        handle1.Complete();

        var handle2 = job2.Schedule(false);
        _jobScheduler.Flush();
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

        _jobScheduler.Flush();

        foreach (var handle in handles) handle.Complete();

        foreach (var handle in handles) handle.Return();

        CollectionAssert.AreEqual(jobs.Select(job => job.Result), Enumerable.Repeat(1, jobCount).ToList());
    }
}