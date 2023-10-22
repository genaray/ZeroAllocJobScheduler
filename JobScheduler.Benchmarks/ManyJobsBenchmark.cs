namespace JobScheduler.Benchmarks;

/// <summary>
/// Benchmark adding a ton of jobs to the queue, flushing, and then completing them.
/// </summary>
[MemoryDiagnoser]
public class ManyJobsBenchmark
{
    private JobScheduler _scheduler = null!;

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

    private JobHandle[] _handles = null!;

    private class EmptyJob : IJob
    {
        public void Execute() { }
    }

    private readonly static EmptyJob _empty = new();

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
        _scheduler = new(config);
        _handles = new JobHandle[ConcurrentJobs];
    }

    [IterationCleanup]
    public void Cleanup()
    {
        _scheduler.Dispose();
    }

    [Benchmark]
    public void BenchmarkSequential()
    {
        for (var w = 0; w < Waves; w++)
        {
            for (var i = 0; i < ConcurrentJobs; i++)
            {
                _handles[i] = _scheduler.Schedule(_empty);
                _scheduler.Flush();
                _handles[i].Complete();
            }
        }
    }

    [Benchmark]
    public void BenchmarkDependancies()
    {
        for (var w = 0; w < Waves; w++)
        {
            for (var i = 0; i < ConcurrentJobs; i++)
            {
                _handles[i] = i == 0 ? _scheduler.Schedule(_empty)
                    : _scheduler.Schedule(_empty, _handles[i - 1]);
            }

            _scheduler.Flush();
            for (var i = 0; i < ConcurrentJobs; i++)
            {
                _handles[i].Complete();
            }
        }
    }
}
