using NUnit.Framework;
using Schedulers.Utils;
using static NUnit.Framework.Assert;

namespace Schedulers.Test;

public class TestJob : IJob
{
    private readonly int _id;
    private readonly Action _action;

    public TestJob(int id, Action action = null)
    {
        _id = id;
        _action = action;
    }

    public void Execute()
    {
        _action?.Invoke();
    }
}

internal class JobSchedulerTests
{
    /// <summary>
    ///     Tests the creation and disposal of a <see cref="JobScheduler"/>.
    /// </summary>
    [Test]
    public void CreateAndDispose()
    {
        using var jobScheduler = new JobScheduler();
    }

    /// <summary>
    ///     Checks whether two jobs with a parent relationship are running at the same time and are terminated appropriately.
    /// </summary>
    [Test]
    public void Flush_ParentWithChild()
    {
        using var jobScheduler = new JobScheduler();

        var job1Completed = new ManualResetEvent(false);
        var job2Completed = new ManualResetEvent(false);

        var job1 = new TestJob(1, () => { job1Completed.Set(); });
        var job2 = new TestJob(2, () => { job2Completed.Set(); });

        // Job2 should finish after Job1 since its his child.
        var handle1 = jobScheduler.Schedule(job1);
        var handle2 = jobScheduler.Schedule(job2, handle1);

        jobScheduler.Flush(handle1);
        jobScheduler.Flush(handle2);

        var job1CompletedFlag = job1Completed.WaitOne(TimeSpan.FromSeconds(5));
        var job2CompletedFlag = job2Completed.WaitOne(TimeSpan.FromSeconds(5));

        IsTrue(job1CompletedFlag, "Job1 did not complete in time.");
        IsTrue(job2CompletedFlag, "Job2 did not complete in time.");
    }

    /// <summary>
    ///     Checks whether a child with a parent relationship runs alone without the parent being executed.
    /// </summary>
    [Test]
    public void Flush_Child()
    {
        using var jobScheduler = new JobScheduler();

        var job1Completed = new ManualResetEvent(false);
        var job2Completed = new ManualResetEvent(false);

        var job1 = new TestJob(1, () => { job1Completed.Set(); });
        var job2 = new TestJob(2, () => { job2Completed.Set(); });

        // Job2 should finish after Job1 since its his child.
        var handle1 = jobScheduler.Schedule(job1);
        var handle2 = jobScheduler.Schedule(job2, handle1);

        jobScheduler.Flush(handle2);

        var job1CompletedFlag = job1Completed.WaitOne(TimeSpan.FromSeconds(5));
        var job2CompletedFlag = job2Completed.WaitOne(TimeSpan.FromSeconds(5));

        IsFalse(job1CompletedFlag, "Job1 did not complete in time.");
        IsTrue(job2CompletedFlag, "Job2 did not complete in time.");
    }

    /// <summary>
    ///     Checks whether both jobs have run when waiting for the parent.
    /// </summary>
    [Test]
    public void Wait_ParentWithChild()
    {
        using var jobScheduler = new JobScheduler();

        var job1Completed = new ManualResetEvent(false);
        var job2Completed = new ManualResetEvent(false);

        var job1 = new TestJob(1, () => { job1Completed.Set(); });
        var job2 = new TestJob(2, () => { job2Completed.Set(); });

        // Both run at the same time, but handle2 is a child of handle1.
        var handle1 = jobScheduler.Schedule(job1);
        var handle2 = jobScheduler.Schedule(job2, handle1);

        jobScheduler.Flush(handle1);
        jobScheduler.Flush(handle2);

        // Waits on handle1 to ensure both handles ran
        jobScheduler.Wait(handle1);

        var job1CompletedFlag = job1Completed.WaitOne(TimeSpan.FromSeconds(5));
        var job2CompletedFlag = job2Completed.WaitOne(TimeSpan.FromSeconds(5));

        IsTrue(job1CompletedFlag, "Job1 did not complete in time.");
        IsTrue(job2CompletedFlag, "Job2 did not complete in time.");
    }

    /// <summary>
    ///     Checks whether all jobs have run when waiting for all of them.
    /// </summary>
    [Test]
    public void Wait_All_ParentWithChild()
    {
        using var jobScheduler = new JobScheduler();

        var job1Completed = new ManualResetEvent(false);
        var job2Completed = new ManualResetEvent(false);

        var job1 = new TestJob(1, () => { job1Completed.Set(); });
        var job2 = new TestJob(2, () => { job2Completed.Set(); });

        // Both run at the same time, but handle2 is a child of handle1.
        var handle1 = jobScheduler.Schedule(job1);
        var handle2 = jobScheduler.Schedule(job2, handle1);

        jobScheduler.Flush(handle1);
        jobScheduler.Flush(handle2);

        // Waits on handle1 to ensure both handles ran
        jobScheduler.Wait(handle1, handle2);

        var job1CompletedFlag = job1Completed.WaitOne(TimeSpan.FromSeconds(5));
        var job2CompletedFlag = job2Completed.WaitOne(TimeSpan.FromSeconds(5));

        IsTrue(job1CompletedFlag, "Job1 did not complete in time.");
        IsTrue(job2CompletedFlag, "Job2 did not complete in time.");
    }

    [Test]
    public void Flush_Dependencies()
    {
        using var jobScheduler = new JobScheduler();

        // AutoResetEvent is better here because it automatically resets to false after a WaitOne call, ensuring the signal is not missed if the job completes quickly.
        var job1Completed = new AutoResetEvent(false);
        var job2Completed = new AutoResetEvent(false);

        var job1 = new TestJob(1, () => { job1Completed.Set(); });
        var job2 = new TestJob(2, () => { job2Completed.Set(); });

        // Job2 should finish after Job1 since its his child.
        var handle1 = jobScheduler.Schedule(job1);
        var handle2 = jobScheduler.Schedule(job2);
        jobScheduler.AddDependency(handle2, handle1);

        jobScheduler.Flush(handle1);

        var job1CompletedFlag = job1Completed.WaitOne(TimeSpan.FromSeconds(5));
        var job2CompletedFlag = job2Completed.WaitOne(TimeSpan.FromSeconds(10));

        IsTrue(job1CompletedFlag, "Job1 did not complete in time.");
        IsTrue(job2CompletedFlag, "Job2 did not complete in time.");
    }
}
