namespace JobScheduler.Benchmarks;

/// <summary>
/// Benchmark the overhead of increasing the <see cref="JobScheduler.Config.MaxExpectedConcurrentJobs"/> without changing the total amount of jobs run.
/// </summary>
[MemoryDiagnoser]
public class MaxConcurrentJobsBenchmark
{
    private JobScheduler Scheduler = null!;

    /// <summary>
    /// The thread count tested
    /// </summary>
    [Params(0)] public int Threads = 0;

    /// <summary>
    /// The amount of total jobs to schedule over the course of the test.
    /// </summary>
    [Params(1024 * 32)] public int TotalJobs;

    /// <summary>
    /// The <see cref="JobScheduler.Config.MaxExpectedConcurrentJobs"/> value. If this is less than <see cref="ConcurrentJobs"/> on a given benchmark,
    /// the benchmark is expected to allocate.
    /// </summary>
    [Params(32, 4096)] public int MaxConcurrentJobs;

    Queue<JobHandle> Handles = null!;
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
            StrictAllocationMode = true,
            ThreadPrefixName = nameof(MaxConcurrentJobsBenchmark),
            ThreadCount = Threads
        };
        Scheduler = new(config);
        Handles = new Queue<JobHandle>(MaxConcurrentJobs);
    }

    [IterationCleanup]
    public void Cleanup()
    {
        Scheduler.Dispose();
    }

    [Benchmark]
    public void BenchmarkDependancies()
    {
        int jobsSoFar = 0;

        // schedule some starter jobs; should have negligible impact on total performance even with varying MaxConcurrentJobs as long as TotalJobs >>> MaxConcurrentJobs
        for (int i = 0; i < MaxConcurrentJobs; i++)
        {
            Handles.Enqueue(Scheduler.Schedule(Empty));
            jobsSoFar++;
        }
        Scheduler.Flush();

        // keep going up until the total job limit of jobs processed
        while (jobsSoFar <= TotalJobs)
        {
            // complete the last-entered job; probably complete by now
            Handles.Dequeue().Complete();
            // schedule a new one to fill the gap
            Handles.Enqueue(Scheduler.Schedule(Empty));
            Scheduler.Flush();
            jobsSoFar++;
        }
    }
}
