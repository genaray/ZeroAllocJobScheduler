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

    Queue<int> Queue = null!;
    ConcurrentQueue<int> ConcurrentQueue = null!;

    [IterationSetup]
    public void Setup()
    {
        Queue = new Queue<int>();
        ConcurrentQueue = new ConcurrentQueue<int>();

        for (int i = 0; i < QueueCapacity; i++)
        {
            ConcurrentQueue.Enqueue(0);
            Queue.Enqueue(0);
        }
        ConcurrentQueue.Clear();
        Queue.Clear();
    }

    [IterationCleanup]
    public void Cleanup()
    {
        ConcurrentQueue = null!;
        Queue = null!;
    }

    [Benchmark]
    public void BenchmarkConcurrentQueue()
    {
        for (int r = 0; r < Reps; r++)
        {
            for (int i = 0; i < QueueCapacity; i++)
            {
                ConcurrentQueue.Enqueue(0);
            }
            ConcurrentQueue.Clear();
        }
    }

    [Benchmark]
    public void BenchmarkConcurrentQueueWithDequeue()
    {
        for (int r = 0; r < Reps; r++)
        {
            for (int i = 0; i < QueueCapacity; i++)
            {
                ConcurrentQueue.Enqueue(0);
            }
            while (ConcurrentQueue.TryDequeue(out var _)) { }
        }
    }

    [Benchmark]
    public void BenchmarkQueue()
    {
        for (int r = 0; r < Reps; r++)
        {
            for (int i = 0; i < QueueCapacity; i++)
            {
                Queue.Enqueue(0);
            }
            Queue.Clear();
        }
    }
}
