using JobScheduler.Test.Utils;

namespace JobScheduler.Test;

internal class JobSchedulerTests : SchedulerTestFixture
{
    public JobSchedulerTests(int threads) : base(threads) { }

    [Test]
    public void EnsureThreadsActuallyExit()
    {
        SuppressDispose = true;
        var numThreads = ThreadCount;
        if (numThreads <= 0) numThreads = Environment.ProcessorCount;
        Thread.Sleep(10); // wait for threads to spawn
        Assert.That(Scheduler.ThreadsAlive, Is.EqualTo(numThreads));
        Scheduler.Dispose();
        Thread.Sleep(10);
        Assert.That(Scheduler.ThreadsAlive, Is.EqualTo(0));
    }

    private class ExceptionJob : IJob
    {
        public Exception? Exception = null;

        public ExceptionJob(Action badCode)
        {
            BadCode = badCode;
        }

        public Action BadCode { get; }

        public void Execute()
        {
            try
            {
                BadCode.Invoke();
            }
            catch (Exception e)
            {
                Exception = e;
            }
        }
    }

    [Test]
    public void CannotFlushOnOtherThread()
    {
        var job = new ExceptionJob(Scheduler.Flush);
        var handle = Scheduler.Schedule(job);
        Scheduler.Flush();
        handle.Complete();

        Assert.That(job.Exception, Is.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void CannotScheduleOnOtherThread()
    {
        var job = new ExceptionJob(() => Scheduler.Schedule(new SleepJob(20)));
        var handle = Scheduler.Schedule(job);
        Scheduler.Flush();
        handle.Complete();

        Assert.That(job.Exception, Is.TypeOf<InvalidOperationException>());
    }

    private class CompleteJob : IJob
    {
        readonly JobHandle _handle;
        public CompleteJob(JobHandle handle)
        {
            _handle = handle;
        }

        public void Execute()
        {
            _handle.Complete();
        }
    }

    [Test]
    public void CanAwaitOtherJob()
    {
        var dependency = new SleepJob(100);
        var job = new CompleteJob(Scheduler.Schedule(dependency));
        var handle = Scheduler.Schedule(job);
        Assert.That(dependency.Result, Is.EqualTo(0));
        Scheduler.Flush();
        handle.Complete();
        Assert.That(dependency.Result, Is.EqualTo(1));
    }
}
