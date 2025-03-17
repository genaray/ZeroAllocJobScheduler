using System.Diagnostics;
using System.Numerics;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.CsProj;
using BenchmarkDotNet.Toolchains.InProcess.NoEmit;
using CommunityToolkit.HighPerformance;
using Schedulers;
using Schedulers.Benchmarks;
using Schedulers.Utils;

namespace Arch.Benchmarks;

public struct VectorCalculationJob : IParallelJobProducer
{
    public float[] a;
    public float[] b;
    public float[] result;

    public int Repetitions;

    public void RunVectorized(int start, int end)
    {
        var vectorSize = Vector<float>.Count;
        var i = start;
        for (; i <= end - vectorSize; i += vectorSize)
        {
            var va = new Vector<float>(a, i);
            var vb = new Vector<float>(b, i);
            var vresult = va + vb;
            for (var r = 1; r < Repetitions; r++)
            {
                vresult += va * vb;
            }

            vresult.CopyTo(result, i);
        }
    }

    public void RunSingle(int index)
    {
        var sum = a[index] + b[index];
        for (var r = 1; r < Repetitions; r++)
        {
            sum += a[index] * b[index];
        }

        result[index] = sum;
    }
}

public struct HeavyCalculationJob : IJob, IParallelJobProducer
{
    private double _first;
    private double _second;

    public HeavyCalculationJob(int first, int second)
    {
        _first = first;
        _second = second;
    }

    public void Execute()
    {
        for (var i = 0; i < 100; i++)
        {
            _first = double.Sqrt(_second);
            _second = double.Sqrt(_first) + 1;
        }
    }

    public void RunVectorized(int index, int end)
    {
        for (var i = index; i < end; i++)
        {
            Execute();
        }
    }

    public void RunSingle(int index)
    {
        throw new NotImplementedException();
    }
}

public struct TestCorrectnessJob : IParallelJobProducer
{
    public static int total = 0;
    public static bool acceptsNewEntries = false;

    public void RunVectorized(int index, int end)
    {
        for (var i = index; i < end; i++)
        {
            RunSingle(index + i);
        }
    }

    public void RunSingle(int index)
    {
        if (!acceptsNewEntries) throw new("Should not accept new entries");
        var newValue = Interlocked.Increment(ref total);
        // Console.WriteLine($" {index} {newValue}");
    }
}

public class JobTimer
{
    private Stopwatch timer;

    public JobTimer()
    {
        timer = Stopwatch.StartNew();
    }

    public long End(int jobs, string type)
    {
        type = type.PadRight(50);
        var time = timer.ElapsedMilliseconds;
        Console.WriteLine($"Time for {type} : {time}ms, Jobs: {jobs}, jobs per second {jobs / ((double)time / 1000) / 1_000_000}M");
        return time;
    }
}

public class Benchmark
{
    private const int jobCount = 200000;
    private const int loopCount = 100;

    private static void CorrectnessTestJob()
    {
        using var jobScheduler = new JobScheduler();
        var timer = new JobTimer();
        for (var sindex = 0; sindex < loopCount; sindex++)
        {
            TestCorrectnessJob.total = 0;
            TestCorrectnessJob.acceptsNewEntries = true;
            var job = new ParallelJobProducer<TestCorrectnessJob>(jobCount, new(), jobScheduler);
            jobScheduler.Wait(job.GetHandle());
            TestCorrectnessJob.acceptsNewEntries = false;
            var expected = jobCount;
            if (TestCorrectnessJob.total != expected)
            {
                throw new($"{TestCorrectnessJob.total} != {expected}");
            }
        }

        timer.End(jobCount * loopCount, "Correctness");
    }

    private static void BenchB()
    {
        using var jobScheduler = new JobScheduler();
        var timer = new JobTimer();
        for (var sindex = 0; sindex < loopCount; sindex++)
        {
            var parentHandle = jobScheduler.Schedule(new EmptyJob());
            for (var index = 0; index < jobCount; index++)
            {
                var job = new HeavyCalculationJob(index, index);
                var handle = jobScheduler.Schedule(job);
                handle.Parent = parentHandle.Index;
                handle.SetDependsOn(parentHandle);
                jobScheduler.Flush(handle);
            }

            jobScheduler.Flush(parentHandle);
            jobScheduler.Wait(parentHandle);
        }

        timer.End(jobCount * loopCount, "Every calculation job is its own handle");
    }

    private static void BenchC()
    {
        using var jobScheduler = new JobScheduler();
        var timer = new JobTimer();
        for (var sindex = 0; sindex < loopCount; sindex++)
        {
            var job = new ParallelJobProducer<HeavyCalculationJob>(jobCount, new(), jobScheduler);
            jobScheduler.Wait(job.GetHandle());
        }

        timer.End(jobCount * loopCount, "ParallelJobProducer");
    }

    private static void BenchD()
    {
        var timer = new JobTimer();
        for (var sindex = 0; sindex < loopCount; sindex++)
        {
            Parallel.For(0, jobCount, i =>
            {
                var job = new HeavyCalculationJob(i, i);
                job.Execute();
            });
        }

        timer.End(jobCount * loopCount, "Just Parallel.For");
    }

    private static long BenchVector(bool dontUseVector)
    {
        using var jobScheduler = new JobScheduler();
        var timer = new JobTimer();
        var data = new VectorCalculationJob { a = new float[jobCount], b = new float[jobCount], result = new float[jobCount], Repetitions = 500 };
        for (var sindex = 0; sindex < loopCount; sindex++)
        {
            var job = new ParallelJobProducer<VectorCalculationJob>(jobCount, data, jobScheduler, 16, !dontUseVector);
            jobScheduler.Wait(job.GetHandle());
        }

        return timer.End(jobCount * loopCount, $"Use vector: {!dontUseVector}");
    }

    private static void Main(string[] args)
    {
        // var config = DefaultConfig.Instance.AddJob(Job.Default
        //     .WithWarmupCount(2)
        //     .WithMinIterationCount(10)
        //     .WithIterationCount(20)
        //     .WithMaxIterationCount(30)
        //     // .WithAffinity(65535)//To not freeze my pc
        // );
        // config = config.WithOptions(ConfigOptions.DisableOptimizationsValidator);
        // BenchmarkRunner.Run<JobSchedulerBenchmark>(config);
        // return;
        for (var i = 0;; i++)
        {
            // CorrectnessTestJob();
            // BenchB();
            // BenchC();
            // BenchD();
            var vectorized = BenchVector(true);
            var nonVectorized = BenchVector(false);
            Console.WriteLine($"Ratio {(double)nonVectorized / vectorized}");
        }
        //using var jobScheduler = new JobScheduler();

        // Spawn massive jobs and wait for finish
        /*
        for (var index = 0; index < 1000; index++)
        {
            var indexCopy = index;
            var job = new TestJob(index, () => { Console.WriteLine($"FINISHED {indexCopy}"); });

            var handle1 = jobScheduler.Schedule(job);
            jobScheduler.Flush(handle1);
        }

        Thread.Sleep(10_000);*/

        /*
        var handles = new JobHandle[180];
        for (var index = 0; index < 180; index++)
        {
            var indexCopy = index;
            var job = new TestJob(index, () =>
            {
                //Thread.Sleep(1000);
                Console.WriteLine($"Timeout {indexCopy}");
            });

            var handle = jobScheduler.Schedule(job);
            handle.id = index;
            handles[index] = handle;
        }

        jobScheduler.Flush(handles);
        jobScheduler.Wait(handles);
        Console.WriteLine("Finished");*/

        /*
        var handles = new List<JobHandle>();
        for (var index = 0; index < 180; index++)
        {
            var indexCopy = index;
            var job = new TestJob(index, () =>
            {
                //Thread.Sleep(1000);
                Console.WriteLine($"Timeout {indexCopy}");
            });

            var dependency = new TestJob(index+1000, () =>
            {
                Console.WriteLine($"Timeout {indexCopy+1000}");
            });

            var handle = jobScheduler.Schedule(job);
            handle.id = index;

            var dependencyHandle = jobScheduler.Schedule(dependency);
            dependencyHandle.id = index;

            jobScheduler.AddDependency(dependencyHandle, handle);
            handles.Add(handle);
            handles.Add(dependencyHandle);
        }

        jobScheduler.Flush(handles.AsSpan());
        jobScheduler.Wait(handles.AsSpan());
        Console.WriteLine("Finished");*/

        // Use: dotnet run -c Release --framework net7.0 -- --job short --filter *BenchmarkClass1*
        //BenchmarkSwitcher.FromAssembly(typeof(Benchmark).Assembly).Run(args, config);
    }
}
