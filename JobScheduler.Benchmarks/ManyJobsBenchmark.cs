namespace JobScheduler.Benchmarks;

/// <summary>
/// Benchmark adding a ton of jobs to the queue, flushing, and then completing them.
/// </summary>
[MemoryDiagnoser]
public class ManyJobsBenchmark
{
    private JobScheduler Scheduler = null!;

    /// <summary>
    /// The thread count tested
    /// </summary>
    [Params(0)] public int Threads = 0;

    /// <summary>
    /// The maximum amount of concurrent jobs active at one time.
    /// </summary>
    [Params(32, 128)] public int ConcurrentJobs;

    /// <summary>
    /// The <see cref="JobScheduler.Config.MaxExpectedConcurrentJobs"/> value. If this is less than <see cref="ConcurrentJobs"/> on a given benchmark,
    /// the benchmark is expected to allocate.
    /// </summary>
    [Params(32, 2048)] public int MaxConcurrentJobs;

    /// <summary>
    /// How many sequences of <see cref="ConcurrentJobs"/> to run.
    /// </summary>
    [Params(1024)] public int Waves;

    JobHandle[] Handles = null!;
    private class EmptyJob : IJob
    {
        public void Execute() { }
    }

    private readonly static EmptyJob Empty = new();

    [IterationSetup]
    public void Setup()
    {
        var config = new JobScheduler.Config
        {
            MaxExpectedConcurrentJobs = MaxConcurrentJobs,
            StrictAllocationMode = false,
            ThreadPrefixName = nameof(ManyJobsBenchmark),
            ThreadCount = Threads
        };
        Scheduler = new(config);
        Handles = new JobHandle[ConcurrentJobs];
    }

    [IterationCleanup]
    public void Cleanup()
    {
        Scheduler.Dispose();
    }

    [Benchmark]
    public void BenchmarkSequential()
    {
        for (int w = 0; w < Waves; w++)
        {
            for (int i = 0; i < ConcurrentJobs; i++)
            {
                Handles[i] = Scheduler.Schedule(Empty);
                Scheduler.Flush();
                Handles[i].Complete();
            }
        }
    }

    [Benchmark]
    public void BenchmarkDependancies()
    {
        for (int w = 0; w < Waves; w++)
        {
            for (int i = 0; i < ConcurrentJobs; i++)
            {
                if (i == 0) Handles[i] = Scheduler.Schedule(Empty);
                else Handles[i] = Scheduler.Schedule(Empty, Handles[i - 1]);
            }
            Scheduler.Flush();
            for (int i = 0; i < ConcurrentJobs; i++)
            {
                Handles[i].Complete();
            }
        }
    }
}
