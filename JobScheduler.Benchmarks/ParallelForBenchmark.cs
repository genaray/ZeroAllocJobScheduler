using Arch.Benchmarks;

namespace JobScheduler.Benchmarks;

/// <summary>
/// Benchmark adding a ton of jobs to the queue, flushing, and then completing them.
/// </summary>
[MemoryDiagnoser]
public abstract class ParallelForBenchmark
{
    private JobScheduler _scheduler = null!;

    /// <summary>
    /// The thread count tested
    /// </summary>
    [Params(0)] public int Threads = 0;

    /// <summary>
    /// The maximum amount of concurrent jobs active at one time.
    /// </summary>
    [Params(32)] public int MaxConcurrentJobs;

    /// <summary>
    /// How many for-loop iterations to run
    /// </summary>
    public abstract int Size { get; }

    /// <summary>
    /// How many sequences of <see cref="Size"/> to run.
    /// </summary>
    public abstract int Waves { get; }

    /// <summary>
    /// The work to run on each parallel iteration.
    /// </summary>
    protected abstract void Work(int index);

    /// <summary>
    /// Any initialization logic that needs to happen before each iteration.
    /// </summary>
    protected abstract void Init();

    /// <summary>
    /// Validate the data after each iteration.
    /// </summary>
    /// <returns>true if the data is valid, false otherwise</returns>
    protected abstract bool Validate();

    private class BasicParallelJob : IJobParallelFor
    {
        private readonly ParallelForBenchmark _benchmark;
        public int ThreadCount
        {
            get => 0;
        }
        public BasicParallelJob(ParallelForBenchmark benchmark)
        {
            _benchmark = benchmark;
        }

        public void Execute(int i)
        {
            _benchmark.Work(i);
        }
    }

    private BasicParallelJob _basicParallel = null!;

    // to give Parallel.For the best chance possible of competing well, we allow it to not allocate
    private static ParallelForBenchmark _currentBenchmarkCache = null!;

    [IterationSetup]
    public void Setup()
    {
        var config = new JobScheduler.Config
        {
            MaxExpectedConcurrentJobs = MaxConcurrentJobs,
            StrictAllocationMode = false,
            ThreadPrefixName = nameof(ParallelForBenchmark),
            ThreadCount = Threads
        };
        _scheduler = new(config);
        _basicParallel = new(this);
        _currentBenchmarkCache = this;
        Init();
    }

    [IterationCleanup]
    public void Cleanup()
    {
        if (!Validate())
        {
            throw new Exception("Something went wrong while running the jobs!");
        }

        _scheduler.Dispose();
    }

    [Benchmark]
    public void BenchmarkForLoop()
    {
        for (var w = 0; w < Waves; w++)
        {
            for (var i = 0; i < Size; i++)
            {
                Work(i);
            }
        }
    }

    private static void DoFor(int i)
    {
        _currentBenchmarkCache.Work(i);
    }

    [Benchmark]
    public void BenchmarkStandardParallelFor()
    {
        for (var w = 0; w < Waves; w++)
        {
            var result = Parallel.For(0, Size, static (int i) => DoFor(i));
        }
    }

    [Benchmark]
    public void BenchmarkParallelFor()
    {
        for (var w = 0; w < Waves; w++)
        {
            var handle = _scheduler.Schedule(_basicParallel, Size);
            _scheduler.Flush();
            handle.Complete();
        }
    }
}
