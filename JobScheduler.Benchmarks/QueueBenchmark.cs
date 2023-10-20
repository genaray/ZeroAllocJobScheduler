namespace JobScheduler.Benchmarks;

[MemoryDiagnoser]
public class QueueBenchmark
{
    /// <summary>
    /// The amount of items in a Queue.
    /// </summary>
    [Params(1, 32, 64, 128, 256, 512)] public int QueueCapacity;

    /// <summary>
    /// The amount of times to repeat the add/clear process
    /// </summary>
    [Params(32)] public int Reps;

    private Queue<int> _queue = null!;

    private ConcurrentQueue<int> _concurrentQueue = null!;

    [IterationSetup]
    public void Setup()
    {
        _queue = new Queue<int>();
        _concurrentQueue = new ConcurrentQueue<int>();

        for (var i = 0; i < QueueCapacity; i++)
        {
            _concurrentQueue.Enqueue(0);
            _queue.Enqueue(0);
        }

        _concurrentQueue.Clear();
        _queue.Clear();
    }

    [IterationCleanup]
    public void Cleanup()
    {
        _concurrentQueue = null!;
        _queue = null!;
    }

    [Benchmark]
    public void BenchmarkConcurrentQueue()
    {
        for (var r = 0; r < Reps; r++)
        {
            for (var i = 0; i < QueueCapacity; i++)
            {
                _concurrentQueue.Enqueue(0);
            }

            _concurrentQueue.Clear();
        }
    }

    [Benchmark]
    public void BenchmarkConcurrentQueueWithDequeue()
    {
        for (var r = 0; r < Reps; r++)
        {
            for (var i = 0; i < QueueCapacity; i++)
            {
                _concurrentQueue.Enqueue(0);
            }

            while (_concurrentQueue.TryDequeue(out var _))
            {
            }
        }
    }

    [Benchmark]
    public void BenchmarkQueue()
    {
        for (var r = 0; r < Reps; r++)
        {
            for (var i = 0; i < QueueCapacity; i++)
            {
                _queue.Enqueue(0);
            }

            _queue.Clear();
        }
    }
}
