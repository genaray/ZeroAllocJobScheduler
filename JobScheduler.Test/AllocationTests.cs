using JobScheduler.Test.Utils;
using JobScheduler.Test.Utils.CustomConstraints;
using System.Diagnostics.CodeAnalysis;

namespace JobScheduler.Test;

[SuppressMessage("Assertion", "NUnit2045:Use Assert.Multiple",
    Justification = "Multiple asserts are not appropriate as later code")]
internal class AllocationTests : SchedulerTestFixture
{
    public AllocationTests(int threads) : base(threads) { }

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
        JobHandle handle2 = default;

        // we expect the very first job to allocate
        Assert.That(() =>
        {
            handle = Scheduler.Schedule(job);
        }, Is.AllocatingMemory());

        Assert.That(() => { Scheduler.Flush(); }, Is.Not.AllocatingMemory());
        Assert.That(() => { handle.Complete(); }, Is.Not.AllocatingMemory());
        Assert.That(() => { handle2 = Scheduler.Schedule(job); }, Is.Not.AllocatingMemory());
        Assert.That(() => { Scheduler.Flush(); }, Is.Not.AllocatingMemory());
        Assert.That(() => { handle2.Complete(); }, Is.Not.AllocatingMemory());
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
        Assert.That(() => { Scheduler.Flush();  }, Is.Not.AllocatingMemory());
        Assert.That(() => { handle1.Complete(); }, Is.Not.AllocatingMemory());
        Assert.That(() => { handle2.Complete(); }, Is.Not.AllocatingMemory());
        Assert.That(() => { handle1 = Scheduler.Schedule(job); }, Is.Not.AllocatingMemory());
        Assert.That(() => { handle2 = Scheduler.Schedule(job); }, Is.Not.AllocatingMemory());
        Assert.That(() => { Scheduler.Flush(); }, Is.Not.AllocatingMemory());
        Assert.That(() => { handle1.Complete(); }, Is.Not.AllocatingMemory());
        Assert.That(() => { handle2.Complete(); }, Is.Not.AllocatingMemory());
    }
}
