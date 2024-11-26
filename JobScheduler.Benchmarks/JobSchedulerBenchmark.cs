using Arch.Benchmarks;
using CommunityToolkit.HighPerformance;
using Schedulers.Utils;

namespace Schedulers.Benchmarks;

public class CalculationJob : IJob
{
    private readonly int _first;
    private readonly int _second;
    public static volatile int Result;
    public static int a;

    public CalculationJob(int first, int second)
    {
        this._first = first;
        this._second = second;
    }

    public void Execute()
    {
        Result = _first + _second;
        Interlocked.Increment(ref a);
    }
}

[MemoryDiagnoser]
public class JobSchedulerBenchmark
{
    private JobScheduler _jobScheduler;
    private List<JobHandle> _jobHandles;

    private static volatile int result = 0;

    [Params(20000, 1000, 50, 10)] public int Jobs;

    [IterationSetup]
    public void Setup()
    {
        _jobScheduler = new JobScheduler();
        _jobHandles = new List<JobHandle>(Jobs);
    }

    [IterationCleanup]
    public void Cleanup()
    {
        _jobScheduler.Dispose();
        _jobHandles.Clear();
    }

    [Benchmark]
    public void BenchmarkJobScheduler()
    {
        for (var index = 0; index < Jobs; index++)
        {
            var job = new HeavyCalculationJob(index, index);
            var handle = _jobScheduler.Schedule(job);
            _jobHandles.Add(handle);
        }

        _jobScheduler.Flush(_jobHandles.AsSpan());
        _jobScheduler.Wait(_jobHandles.AsSpan());
    }

    [Benchmark]
    public void BenchmarkParallelFor()
    {
        Parallel.For(0, Jobs, i =>
        {
            var job = new HeavyCalculationJob(i, i);
            job.Execute();
        });
    }

    [Benchmark]
    public void BenchmarkJobSchedulerNoAlloc()
    {
        var generation = _jobScheduler.GetNewGeneration();
        for (var index = 0; index < Jobs; index++)
        {
            var job = new HeavyCalculationJob(index, index);
            _jobScheduler.ScheduleAndFlushPooledJobWithGeneration(job, generation);
        }
        _jobScheduler.AwaitForGeneration(generation);
    }
}
