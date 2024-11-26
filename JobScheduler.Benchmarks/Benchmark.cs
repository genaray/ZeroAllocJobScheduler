using System.Diagnostics;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.CsProj;
using BenchmarkDotNet.Toolchains.InProcess.NoEmit;
using CommunityToolkit.HighPerformance;
using Schedulers;
using Schedulers.Benchmarks;
using Schedulers.Utils;

namespace Arch.Benchmarks;

public class HeavyCalculationJob : IJob
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
        for (var i = 0; i < 2000; i++)
        {
            _first = double.Sqrt(_second);
            _second = double.Sqrt(_first) + 1;
        }
    }
}

public class Benchmark
{
    private static void BenchA()
    {
        var timer = Stopwatch.StartNew();
        using var jobScheduler = new JobScheduler();
        for (var sindex = 0; sindex < 100; sindex++)
        {
            var generation = jobScheduler.GetNewGeneration();
            for (var index = 0; index < 20000; index++)
            {
                var job = new HeavyCalculationJob(index, index);
                jobScheduler.ScheduleAndFlushPooledJobWithGeneration(job, generation);
            }

            jobScheduler.AwaitForGeneration(generation);
        }

        var time = timer.ElapsedMilliseconds;
        Console.WriteLine($"Time: {time}ms");
    }

    private static void BenchB()
    {
        using var jobScheduler = new JobScheduler();
        var timer = Stopwatch.StartNew();
        for (var sindex = 0; sindex < 100; sindex++)
        {
            List<JobHandle> _jobHandles = new();
            for (var index = 0; index < 20000; index++)
            {
                var job = new HeavyCalculationJob(index, index);
                var handle = jobScheduler.Schedule(job);
                _jobHandles.Add(handle);
            }

            jobScheduler.Flush(_jobHandles.AsSpan());
            jobScheduler.Wait(_jobHandles.AsSpan());
        }

        var time = timer.ElapsedMilliseconds;
        Console.WriteLine($"Job scheduler Time: {time}ms");
    }

    private static void BenchC()
    {
        var timer = Stopwatch.StartNew();
        for (var sindex = 0; sindex < 100; sindex++)
        {
            var list = new List<HeavyCalculationJob>();
            for (var index = 0; index < 20000; index++)
            {
                var job = new HeavyCalculationJob(index, index);
                list.Add(job);
            }

            Parallel.ForEach(list, job => job.Execute());
        }

        var time = timer.ElapsedMilliseconds;
        Console.WriteLine($"Parallel foreach Time: {time}ms");
    }

    private static void BenchD()
    {
        var timer = Stopwatch.StartNew();
        for (var sindex = 0; sindex < 100; sindex++)
        {
            Parallel.For(0, 20000, i =>
            {
                var job = new HeavyCalculationJob(i, i);
                job.Execute();
            });
        }

        var time = timer.ElapsedMilliseconds;
        Console.WriteLine($"Parallel for Time: {time}ms");
    }

    private static void Main(string[] args)
    {
        var config = DefaultConfig.Instance.AddJob(Job.Default
            .WithWarmupCount(2)
            .WithMinIterationCount(10)
            .WithIterationCount(20)
            .WithMaxIterationCount(30)
            // .WithAffinity(65535)//To not freeze my pc
        );
        config = config.WithOptions(ConfigOptions.DisableOptimizationsValidator);
        BenchmarkRunner.Run<JobSchedulerBenchmark>(config);
        return;
        // var jb = new JobSchedulerBenchmark();
        // jb.Setup();
        // jb.Jobs = 512;
        // jb.BenchmarkJobSchedulerNoAlloc();
        // jb.Cleanup();
        // return;
        for (int i = 0; i < 10; i++)
        {
            BenchB();
            BenchC();
            BenchD();
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
