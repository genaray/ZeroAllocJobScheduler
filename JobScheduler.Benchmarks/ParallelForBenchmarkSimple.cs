namespace Schedulers.Benchmarks;

/// <summary>
/// Increments a simple counter as the work;
/// </summary>
[MemoryDiagnoser]
public class ParallelForBenchmarkSimple : ParallelForBenchmark
{
    private static int _counter;
    public override int Size { get => 1024 * 1024; }
    public override int Waves { get => 32; }
    protected override int BatchSize { get => 1; }

    protected override void Init()
    {
        _counter = 0;
    }

    protected override bool Validate()
    {
        return _counter == Waves * Size;
    }

    protected override void Work(int i)
    {
        Interlocked.Increment(ref _counter);
    }
}
