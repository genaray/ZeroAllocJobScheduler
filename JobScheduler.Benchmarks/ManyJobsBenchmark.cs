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
    /// Whether the benchmark should do a dry run before starting the benchmark, to fill up any data structures that might allocate
    /// on frame 1.
    /// </summary>
    [Params(true)] public bool ExcludeFirstWave;

    /// <summary>
    /// The maximum amount of concurrent jobs active at one time.
    /// </summary>
    [Params(128)] public int ConcurrentJobs;

    /// <summary>
    /// How many sequences of <see cref="ConcurrentJobs"/> to run.
    /// </summary>
    [Params(10240)] public int Waves;

    JobHandle[] Handles = null!;
    private class EmptyJob : IJob
    {
        public void Execute() { }
    }

    private readonly static EmptyJob Empty = new();

    [IterationSetup]
    public void Setup()
    {
        Scheduler = new(nameof(ManyJobsBenchmark), Threads);
        Handles = new JobHandle[ConcurrentJobs];

        if (ExcludeFirstWave)
        {
            Waves--;
            for (int i = 0; i < ConcurrentJobs; i++)
            {
                Handles[i] = Scheduler.Schedule(Empty);
            }
            Scheduler.Flush();
            for (int i = 0; i < ConcurrentJobs; i++)
            {
                Handles[i].Complete();
            }
            for (int i = 0; i < ConcurrentJobs; i++)
            {
                Handles[i].Return();
            }
        }
    }

    [IterationCleanup]
    public void Cleanup()
    {
        Scheduler.Dispose();
    }

    [Benchmark]
    public void BenchmarkParallel()
    {
        for (int w = 0; w < Waves; w++)
        {
            for (int i = 0; i < ConcurrentJobs; i++)
            {
                Handles[i] = Scheduler.Schedule(Empty);
            }
            Scheduler.Flush();
            for (int i = 0; i < ConcurrentJobs; i++)
            {
                Handles[i].Complete();
            }
            for (int i = 0; i < ConcurrentJobs; i++)
            {
                Handles[i].Return();
            }
        }
    }
}
