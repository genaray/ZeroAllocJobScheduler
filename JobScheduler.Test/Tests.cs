using JobScheduler.Extensions;

namespace JobScheduler.Test;

public class Job : IJob {

    public int result = 0;
    public void Execute() {
        Thread.Sleep(25);
        result = 1;
    }
}

public class Tests {

    private JobScheduler jobScheduler;

    [SetUp]
    public void Setup() {
        jobScheduler = new JobScheduler("Test");
    }

    [TearDown]
    public void Clean() {
        jobScheduler.Dispose();
    }

    [Test]
    public void Complete() {
        
        var job = new Job();
        var handle = job.Schedule(false);
        
        jobScheduler.Flush();
        handle.Complete();

        Assert.AreEqual(1, job.result);
    }
}