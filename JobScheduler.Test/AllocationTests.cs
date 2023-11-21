using System.Diagnostics.CodeAnalysis;
using Schedulers.Test.Utils;
using Schedulers.Test.Utils.CustomConstraints;

namespace Schedulers.Test;

[SuppressMessage("Assertion", "NUnit2045:Use Assert.Multiple",
    Justification = "Multiple asserts are not appropriate as later code")]
[SuppressMessage("Style", "IDE0053:Use expression body for lambda expression",
    Justification = "Lambda block bodies are necessary for NUnit to recognize the lambda as a TestDelegate.")]
[SuppressMessage("Style", "IDE0200:Remove unnecessary lambda expression",
    Justification = "Lambda block bodies are necessary for NUnit to recognize the lambda as a TestDelegate.")]
[SuppressMessage("Style", "IDE0002:Name can be simplified",
    Justification = "While NUnit.Framework.Is.Not is lexically simpler, it is stylistically more complex.")]
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
    [TestCase(true)]
    [TestCase(false)]
    public void RegularJobDoesNotAllocate(bool manuallyComplete)
    {
        var job = new SleepJob(2); // allocates

        JobHandle handle = default;
        JobHandle handle2 = default;

        Assert.That(() => { handle = Scheduler.Schedule(job); }, Is.Not.AllocatingMemory());
        Assert.That(() => { Scheduler.Flush(); }, Is.Not.AllocatingMemory());
        if (manuallyComplete)
        {
            Assert.That(() => { handle.Complete(); }, Is.Not.AllocatingMemory());
        }
        else
        {
            Thread.Sleep(100);
        }

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

        Assert.That(() => { handle1 = Scheduler.Schedule(job); }, Is.Not.AllocatingMemory());
        Assert.That(() => { handle2 = Scheduler.Schedule(job); }, Is.Not.AllocatingMemory());
        Assert.That(() => { Scheduler.Flush();  }, Is.Not.AllocatingMemory());
        Assert.That(() => { handle1.Complete(); }, Is.Not.AllocatingMemory());
        Assert.That(() => { handle2.Complete(); }, Is.Not.AllocatingMemory());
        Assert.That(() => { handle1 = Scheduler.Schedule(job); }, Is.Not.AllocatingMemory());
        Assert.That(() => { handle2 = Scheduler.Schedule(job); }, Is.Not.AllocatingMemory());
        Assert.That(() => { Scheduler.Flush(); }, Is.Not.AllocatingMemory());
        Assert.That(() => { handle2.Complete(); }, Is.Not.AllocatingMemory());
        Assert.That(() => { handle1.Complete(); }, Is.Not.AllocatingMemory());
    }

    [Test]
    public void DependentJobsDoNotAllocate()
    {
        var job = new SleepJob(5); // allocates

        JobHandle handle1 = default;
        JobHandle handle2 = default;

        Assert.That(() => { handle1 = Scheduler.Schedule(job); }, Is.Not.AllocatingMemory());
        Assert.That(() => { handle2 = Scheduler.Schedule(job, handle1); }, Is.Not.AllocatingMemory());
        Assert.That(() => { Scheduler.Flush(); }, Is.Not.AllocatingMemory());
        Assert.That(() => { handle1.Complete(); }, Is.Not.AllocatingMemory());
        Assert.That(() => { handle2.Complete(); }, Is.Not.AllocatingMemory());
        Assert.That(() => { handle1 = Scheduler.Schedule(job); }, Is.Not.AllocatingMemory());
        Assert.That(() => { handle2 = Scheduler.Schedule(job, handle1); }, Is.Not.AllocatingMemory());
        Assert.That(() => { Scheduler.Flush(); }, Is.Not.AllocatingMemory());
        Assert.That(() => { handle2.Complete(); }, Is.Not.AllocatingMemory());
        Assert.That(() => { handle1.Complete(); }, Is.Not.AllocatingMemory());
    }

    [Test]
    public void CombinedJobsDoNotAllocate()
    {
        var job = new SleepJob(5); // allocates
        var handles = new JobHandle[2];
        JobHandle combined = default;

        Assert.That(() => { handles[0] = Scheduler.Schedule(job); }, Is.Not.AllocatingMemory());
        Assert.That(() => { handles[1] = Scheduler.Schedule(job); }, Is.Not.AllocatingMemory());
        Assert.That(() => { combined = Scheduler.CombineDependencies(handles); }, Is.Not.AllocatingMemory());
        Assert.That(() => { Scheduler.Flush(); }, Is.Not.AllocatingMemory());
        Assert.That(() => { combined.Complete(); }, Is.Not.AllocatingMemory());
        Assert.That(() => { handles[0] = Scheduler.Schedule(job); }, Is.Not.AllocatingMemory());
        Assert.That(() => { handles[1] = Scheduler.Schedule(job); }, Is.Not.AllocatingMemory());
        Assert.That(() => { combined = Scheduler.CombineDependencies(handles); }, Is.Not.AllocatingMemory());
        Assert.That(() => { Scheduler.Flush(); }, Is.Not.AllocatingMemory());
        Assert.That(() => { combined.Complete(); }, Is.Not.AllocatingMemory());
        Assert.That(() => { handles[0].Complete(); }, Is.Not.AllocatingMemory());
        Assert.That(() => { handles[1].Complete(); }, Is.Not.AllocatingMemory());
    }

    [Test]
    [TestCase(32, 10000)]
    public void StrictModeThrowsCorrectly(int jobs, int waves)
    {
        var job = new SleepJob(0);
        var handles = new JobHandle[jobs];

        Assert.That(() =>
        {
            for (var w = 0; w < waves; w++)
            {
                for (var i = 0; i < jobs; i++)
                {
                    handles[i] = Scheduler.Schedule(job);
                }

                Scheduler.Flush();
                for (var i = 0; i < jobs; i++)
                {
                    handles[i].Complete();
                }
            }

            for (var i = 0; i < jobs; i++)
            {
                handles[i] = Scheduler.Schedule(job);
            }
        }, Is.Not.AllocatingMemory());

        Assert.That(() => { var badHandle = Scheduler.Schedule(job); },
            Throws.Exception.TypeOf<JobScheduler.MaximumConcurrentJobCountExceededException>());
    }
}
[TestFixture(0, 32)]
[TestFixture(1, 32)]
[TestFixture(2, 32)]
[TestFixture(4, 32)]
[TestFixture(8, 32)]
[TestFixture(16, 32)]
[TestFixture(0, 128)]
[TestFixture(1, 128)]
[TestFixture(2, 128)]
[TestFixture(4, 128)]
[TestFixture(8, 128)]
[TestFixture(16, 128)]
[SuppressMessage("Assertion", "NUnit2045:Use Assert.Multiple",
    Justification = "Multiple asserts are not appropriate as later code")]
[SuppressMessage("Style", "IDE0002:Name can be simplified",
    Justification = "While NUnit.Framework.Is.Not is lexically simpler, it is stylistically more complex.")]
internal class NonStrictAllocationTests : SchedulerTestFixture
{
    public NonStrictAllocationTests(int threads, int maxJobs) : base(threads)
    {
        MaxExpectedConcurrentJobs = maxJobs;
    }

    protected override bool StrictAllocationMode
    {
        get => false;
    }

    protected override int MaxExpectedConcurrentJobs { get; }

    [Test]
    [TestCase(100)]
    public void NonStrictModeAllocatesCorrectly(int waves)
    {
        var jobs = MaxExpectedConcurrentJobs;
        var job = new SleepJob(0);
        var handles = new JobHandle[jobs];
        Assert.That(() =>
        {
            for (var w = 0; w < waves; w++)
            {
                for (var i = 0; i < jobs; i++)
                {
                    handles[i] = Scheduler.Schedule(job);
                }

                Scheduler.Flush();
                for (var i = 0; i < jobs; i++)
                {
                    handles[i].Complete();
                }
            }
        }, Is.Not.AllocatingMemory());

        Assert.That(() =>
        {
            for (var i = 0; i < jobs; i++)
            {
                handles[i] = Scheduler.Schedule(job);
            }
        }, Is.Not.AllocatingMemory());

        Assert.That(() => { var badHandle = Scheduler.Schedule(job); },
            Is.AllocatingMemory());
    }
}
