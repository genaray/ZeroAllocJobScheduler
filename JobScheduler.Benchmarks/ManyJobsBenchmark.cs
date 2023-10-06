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
    [Params(true, false)] public bool SetupJobsFirst;

    /// <summary>
    /// The amount of jobs to run.
    /// </summary>
    [Params(10, 100, 1000000)] public int JobCount;

    JobHandle[] Handles = null!;
    private class EmptyJob : IJob
    {
        public void Execute() { }
    }

    private static IJob Empty => new EmptyJob();

    [IterationSetup]
    public void Setup()
    {
        Scheduler = new(nameof(ManyJobsBenchmark), Threads);
        Handles = new JobHandle[JobCount];

        if (SetupJobsFirst)
        {
            for (int i = 0; i < JobCount; i++)
            {
                Handles[i] = Scheduler.Schedule(Empty);
            }
            Scheduler.Flush();
            for (int i = 0; i < JobCount; i++)
            {
                Handles[i].Complete();
            }
            for (int i = 0; i < JobCount; i++)
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
        for (int i = 0; i < JobCount; i++)
        {
            Handles[i] = Scheduler.Schedule(Empty);
        }
        Scheduler.Flush();
        for (int i = 0; i < JobCount; i++)
        {
            Handles[i].Complete();
        }
        for (int i = 0; i < JobCount; i++)
        {
            Handles[i].Return();
        }
    }
}
