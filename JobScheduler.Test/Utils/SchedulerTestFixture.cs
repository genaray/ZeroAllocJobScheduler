namespace JobScheduler.Test.Utils;

[TestFixture]
[NonParallelizable]
internal class SchedulerTestFixture
{
    protected JobScheduler Scheduler { get; private set; } = null!;

    [SetUp]
    public void Setup()
    {
        Scheduler = new JobScheduler("Test");
    }

    [TearDown]
    public void Clean()
    {
        Scheduler.Dispose();
        Scheduler = null!;
    }
}
