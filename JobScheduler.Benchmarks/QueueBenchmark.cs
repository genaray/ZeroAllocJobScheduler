namespace JobScheduler.Benchmarks;

[MemoryDiagnoser]
public class QueueBenchmark
{
    /// <summary>
    /// The amount of items in a Queue.
    /// </summary>
    [Params(10, 100, 5000000)] public int QueueCapacity;

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

    private static int Nothing = 0;

    [Benchmark]
    public void TestEmptyBenchmark()
    {
        for (int i = 0; i < QueueCapacity; i++)
        {
            Nothing++;
        }
    }

    [Benchmark]
    public void TestConcurrentQueue()
    {
        for (int i = 0; i < QueueCapacity; i++)
        {
            ConcurrentQueue.Enqueue(0);
        }
    }

    [Benchmark]
    public void TestQueue()
    {
        for (int i = 0; i < QueueCapacity; i++)
        {
            Queue.Enqueue(0);
        }
    }
}
