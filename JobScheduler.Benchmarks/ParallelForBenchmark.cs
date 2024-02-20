using Arch.Benchmarks;
using Schedulers;

namespace Schedulers.Benchmarks;

/// <summary>
/// Benchmark adding a ton of jobs to the queue, flushing, and then completing them.
/// </summary>
[MemoryDiagnoser]
public abstract class ParallelForBenchmark
{
    private Schedulers.JobScheduler _scheduler = null!;

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
    /// The batch size to use for the jobs.
    /// </summary>
    protected abstract int BatchSize { get; }

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
        public int BatchSize { get => _benchmark.BatchSize; }

        public BasicParallelJob(ParallelForBenchmark benchmark)
        {
            _benchmark = benchmark;
        }

        public void Execute(int i)
        {
            _benchmark.Work(i);
        }

        public void Finish() { }
    }

    private BasicParallelJob _basicParallel = null!;

    private class BasicRegularJob : IJob
    {
        private readonly ParallelForBenchmark _benchmark;
        private readonly int _start;
        private readonly int _size;
        public BasicRegularJob(ParallelForBenchmark benchmark, int start, int size)
        {
            _benchmark = benchmark;
            _start = start;
            _size = size;
        }

        public void Execute()
        {
            for (var i = _start; i < _start + _size; i++)
            {
                _benchmark.Work(i);
            }
        }
    }

    private List<BasicRegularJob> _basicRegulars = null!;
    private List<JobHandle> _jobHandles = null!;

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
        _basicRegulars = new(_scheduler.ThreadCount);

        var remaining = Size;
        var amountPerThread = Size / _scheduler.ThreadCount;
        for (var i = 0; i < _scheduler.ThreadCount; i++)
        {
            var amount = amountPerThread;
            if (remaining < amount)
            {
                amount = remaining;
            }

            if (remaining == 0)
            {
                break;
            }

            _basicRegulars.Add(new(this, i * amountPerThread, amount));
            remaining -= amount;
        }

        _jobHandles = new(_scheduler.ThreadCount);
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
    public void BenchmarkDotNetParallelFor()
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

    [Benchmark]
    public void BenchmarkNaiveParallelFor()
    {
        for (var w = 0; w < Waves; w++)
        {
            foreach (var job in _basicRegulars)
            {
                _jobHandles.Add(_scheduler.Schedule(job));
            }

            _scheduler.Flush();

            foreach (var handle in _jobHandles)
            {
                handle.Complete();
            }

            _jobHandles.Clear();
        }
    }
}
