using JobScheduler.Test.Utils;

namespace JobScheduler.Test;

internal class CombineDependenciesTests : SchedulerTestFixture
{
    public CombineDependenciesTests(int threads) : base(threads) { }

    private void CombineTwoDependencies(JobHandle[] cachedList)
    {
        Assert.That(cachedList, Has.Length.EqualTo(2));
        var job1 = new SleepJob(10);
        var job2 = new SleepJob(10);
        ActionJob job3 = null!;
        job3 = new ActionJob(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(job1.Result, Is.EqualTo(1));
                Assert.That(job2.Result, Is.EqualTo(1));
                Assert.That(job3.Result, Is.EqualTo(0));
            });
        });

        var job1Handle = Scheduler.Schedule(job1);
        var job2Handle = Scheduler.Schedule(job2);
        cachedList[0] = job1Handle;
        cachedList[1] = job2Handle;
        var job1And2Handle = Scheduler.CombineDependencies(cachedList);
        var job3Handle = Scheduler.Schedule(job3, job1And2Handle);

        Assert.Multiple(() =>
        {
            Assert.That(job1.Result, Is.EqualTo(0));
            Assert.That(job2.Result, Is.EqualTo(0));
            Assert.That(job3.Result, Is.EqualTo(0));
        });

        Scheduler.Flush();
        job3Handle.Complete();

        Assert.Multiple(() =>
        {
            Assert.That(job1.Result, Is.EqualTo(1));
            Assert.That(job2.Result, Is.EqualTo(1));
            Assert.That(job3.Result, Is.EqualTo(1));
        });
    }

    [Test]
    public void CombineTwoDependenciesFunctions()
    {
        CombineTwoDependencies(new JobHandle[2]);
    }

    [Test]
    public void CombineTwoDependenciesCanReuseList()
    {
        var list = new JobHandle[2];
        for (int i = 0; i < 3; i++)
        {
            CombineTwoDependencies(list);
        }
    }

    private struct DependencyChainElement
    {
        public DependencyChainElement() { }

        public List<JobHandle> Handles { get; set; } = new();
        public List<SleepJob> Jobs { get; set; } = new();
        public JobHandle ChainHandle { get; set; }
    }

    private List<DependencyChainElement> CreateDependencyChain(int chainLength, int jobCountPerChainLink, bool flushAfterEveryLink)
    {
        // this constructs a form like:
        // [a, b, c] => [d, e f] => [g, h, i] ...
        // inside the brackets, things can run in parallel, but => marks a hard dependency where ex. [a, b, c] must all complete before [d, e, f].
        // chainLength determines how many => dependencies there are, and jobCountPerChainLink controls how many letters per bracket section.

        // this should be kept as low as possible to avoid long test times.
        // the goal is to get it as small as possible without the threads outrunning the main thread (because if that happens, everything will
        // complete without us testing whether it's completing in the right order!)
        int timeout = 5;

        List<DependencyChainElement> chain = new();
        for (int i = 0; i < chainLength; i++)
        {
            DependencyChainElement link = new();
            for (int j = 0; j < jobCountPerChainLink; j++)
            {
                link.Jobs.Add(new SleepJob(timeout));
                // each job in this link has a dependency on the entirety of the last link's CombineDependencies
                if (chain.Any()) link.Handles.Add(Scheduler.Schedule(link.Jobs.Last(), chain.Last().ChainHandle));
                // first one doesn't need any dependencies on the last CombineDependencies
                else link.Handles.Add(Scheduler.Schedule(link.Jobs.Last()));
            }

            link.ChainHandle = Scheduler.CombineDependencies(link.Handles.ToArray());

            chain.Add(link);
            if (flushAfterEveryLink) Scheduler.Flush();
        }

        if (!flushAfterEveryLink) Scheduler.Flush();

        return chain;
    }

    private static void CheckChainProgress(List<DependencyChainElement> chain, int linksComplete)
    {
        // check the jobs in the chain that are complete at the given step
        foreach (var link in chain)
        {
            linksComplete--;
            foreach (var job in link.Jobs)
            {
                if (linksComplete >= 0) Assert.That(job.Result, Is.EqualTo(1));
                else Assert.That(job.Result, Is.EqualTo(0));
            }
        }
    }

    [Test]
    [TestCase(1, 5, true)]
    [TestCase(1, 5, false)]
    [TestCase(5, 1, true)]
    [TestCase(5, 1, false)]
    [TestCase(5, 5, true)]
    [TestCase(5, 5, false)]
    public void TestDependencyChainForwards(int chainLength, int jobCountPerChainLink, bool flushAfterEveryLink)
    {
        var chain = CreateDependencyChain(chainLength, jobCountPerChainLink, flushAfterEveryLink);

        // complete each link in sequence and make sure our progress lines up
        int progress = 0;
        foreach (var link in chain)
        {
            CheckChainProgress(chain, progress);
            link.ChainHandle.Complete();
            progress++;
        }

        // do one last check to make sure we're all complete
        CheckChainProgress(chain, progress);
    }

    [Test]
    [TestCase(1, 5, true)]
    [TestCase(1, 5, false)]
    [TestCase(5, 1, true)]
    [TestCase(5, 1, false)]
    [TestCase(5, 5, true)]
    [TestCase(5, 5, false)]
    public void TestDependencyChainReverse(int chainLength, int jobCountPerChainLink, bool flushAfterEveryLink)
    {
        var chain = CreateDependencyChain(chainLength, jobCountPerChainLink, flushAfterEveryLink);
        chain.Reverse();

        // complete each link from the bottom-up and make sure our progress lines up
        // everything should be always complete
        CheckChainProgress(chain, 0);
        foreach (var link in chain)
        {
            link.ChainHandle.Complete();
            CheckChainProgress(chain, chain.Count);
        }
    }
}
