using JobScheduler.Test.Utils;
using JobScheduler.Test.Utils.CustomConstraints;
using System.Diagnostics.CodeAnalysis;

namespace JobScheduler.Test;


[TestFixture]
[SuppressMessage("Assertion", "NUnit2045:Use Assert.Multiple",
    Justification = "Multiple asserts are not appropriate as later code")]
internal class AllocationTests : SchedulerTestFixture
{
    // Note: in the tests do NOT use job.Schedule(); it is not threadsafe since it uses the singleton.
    // Use Scheduler.Schedule() from the fixture.

    private class TestClass
    {
        public string Data = "Some data here.";
    }

    private struct TestStruct
    {
        public long Data = 0xDEADBEEF;

        public TestStruct() { }
    }

    [Test]
    public void CreatingClassDoesAllocate()
    {
        // sanity test for allocation fixture
        Assert.That(() =>
        {
            _ = new TestClass();
        }, Is.AllocatingMemory());
    }

    [Test]
    public void CreatingStructDoesNotAllocate()
    {
        // sanity test for allocation fixture
        Assert.That(() =>
        {
            _ = new TestStruct();
        }, Is.Not.AllocatingMemory());
    }

    [Test]
    public void RegularJobDoesNotAllocate()
    {
        var job = new SleepJob(100); // allocates

        JobHandle handle = default;

        // we expect the very first job to allocate
        Assert.That(() =>
        {
            handle = Scheduler.Schedule(job);
        }, Is.AllocatingMemory());

        Assert.That(() =>
        {
            Scheduler.Flush();
            handle.Complete();
            handle.Return();

            var handle2 = Scheduler.Schedule(job);
            Scheduler.Flush();
            handle2.Complete();
            handle2.Return();
        }, Is.Not.AllocatingMemory());
    }

    [Test]
    [NonParallelizable]
    public void AutoPooledJobDoesNotAllocate()
    {
        var job = new SleepJob(5); // allocates
        // we expect the very first job to allocate
        Assert.That(() =>
        {
            Scheduler.Schedule(job, true);
        }, Is.AllocatingMemory());

        // the rest of everything should not allocate
        Assert.That(() =>
        {
            Scheduler.Flush();
            // ensure it's disposed
            Thread.Sleep(100);

            // try another one
            Scheduler.Schedule(job, true);
            Scheduler.Flush();
            Thread.Sleep(100);
        }, Is.Not.AllocatingMemory());
    }

    [Test]
    public void SimultaneousJobsDoNotAllocate()
    {
        var job = new SleepJob(5); // allocates

        JobHandle handle1 = default;
        JobHandle handle2 = default;
        // we expect the first 2 jobs to allocate
        Assert.That(() => { handle1 = Scheduler.Schedule(job); }, Is.AllocatingMemory());
        Assert.That(() => { handle2 = Scheduler.Schedule(job); }, Is.AllocatingMemory());

        // the rest of everything should not allocate
        Assert.That(() =>
        {
            Scheduler.Flush();
            handle1.Complete();
            handle2.Complete();
            handle1.Return();
            handle2.Return();

            handle1 = Scheduler.Schedule(job);
            handle2 = Scheduler.Schedule(job);
            Scheduler.Flush();
            handle1.Complete();
            handle2.Complete();
            handle1.Return();
            handle2.Return();
        }, Is.Not.AllocatingMemory());
    }
}
