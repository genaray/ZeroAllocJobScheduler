using JobScheduler.Test.Utils;

namespace JobScheduler.Test;

internal partial class SingleDependencyTests : SchedulerTestFixture
{
    public SingleDependencyTests(int threads) : base(threads) { }
    [Test]
    [TestCase(1, 10)]
    [TestCase(10, 1)]
    [TestCase(5, 5)]
    public void OneDependencyFunctions(int firstDuration, int secondDuration)
    {
        ActionJob job1 = null!;
        ActionJob job2 = null!;
        job1 = new ActionJob(() =>
        {
            Thread.Sleep(firstDuration);
            Assert.Multiple(() =>
            {
                Assert.That(job1.Result, Is.EqualTo(0));
                Assert.That(job2.Result, Is.EqualTo(0));
            });
        });
        job2 = new ActionJob(() =>
        {
            Thread.Sleep(secondDuration);
            Assert.Multiple(() =>
            {
                Assert.That(job1.Result, Is.EqualTo(1));
                Assert.That(job2.Result, Is.EqualTo(0));
            });
        });
        var handle1 = Scheduler.Schedule(job1);
        var handle2 = Scheduler.Schedule(job2, handle1);
        Scheduler.Flush();

        handle2.Complete();
        Assert.Multiple(() =>
        {
            Assert.That(job1.Result, Is.EqualTo(1));
            Assert.That(job2.Result, Is.EqualTo(1));
        });
        handle1.Complete();
        Assert.Multiple(() =>
        {
            Assert.That(job1.Result, Is.EqualTo(1));
            Assert.That(job2.Result, Is.EqualTo(1));
        });
    }

    [Test]
    [TestCase(true)]
    [TestCase(false)]
    public void OneDependencyFunctionsAfterCompletion(bool manuallyComplete)
    {
        ActionJob job1 = null!;
        ActionJob job2 = null!;
        job1 = new ActionJob(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(job1.Result, Is.EqualTo(0));
                Assert.That(job2.Result, Is.EqualTo(0));
            });
        });
        job2 = new ActionJob(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(job1.Result, Is.EqualTo(1));
                Assert.That(job2.Result, Is.EqualTo(0));
            });
        });
        var handle1 = Scheduler.Schedule(job1);
        Scheduler.Flush();

        if (manuallyComplete) handle1.Complete();
        else Thread.Sleep(10);

        Assert.Multiple(() =>
        {
            Assert.That(job1.Result, Is.EqualTo(1));
            Assert.That(job2.Result, Is.EqualTo(0));
        });

        var handle2 = Scheduler.Schedule(job2, handle1);
        Scheduler.Flush();
        if (manuallyComplete) handle2.Complete();
        else Thread.Sleep(10);

        Assert.Multiple(() =>
        {
            Assert.That(job1.Result, Is.EqualTo(1));
            Assert.That(job2.Result, Is.EqualTo(1));
        });
    }
}
