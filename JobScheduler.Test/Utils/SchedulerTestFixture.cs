namespace JobScheduler.Test.Utils;


// run the fixture with multiple thread configurations
[TestFixture(0)]
[TestFixture(1)]
[TestFixture(2)]
[TestFixture(4)]
[TestFixture(8)]
[TestFixture(16)]
internal class SchedulerTestFixture
{
    protected JobScheduler Scheduler { get; private set; } = null!;

    protected int ThreadCount { get; }

    protected bool SuppressDispose { get; set; } = false;

    public SchedulerTestFixture(int threads)
    {
        ThreadCount = threads;
    }

    [SetUp]
    public void Setup()
    {
        Scheduler = new JobScheduler("Test", ThreadCount);
    }

    [TearDown]
    public void Clean()
    {
        if (!SuppressDispose)
            Scheduler.Dispose();
        Scheduler = null!;
    }
}
