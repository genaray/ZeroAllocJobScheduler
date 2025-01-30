using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.CsProj;
using BenchmarkDotNet.Toolchains.InProcess.NoEmit;
using CommunityToolkit.HighPerformance;
using Schedulers;
using Schedulers.Benchmarks;
using Schedulers.Utils;

namespace Arch.Benchmarks;

public class Benchmark
{
    private static void Main(string[] args)
    {
        /*
        for (var sindex = 0; sindex < 1_000; sindex++)
        {
            var jobScheduler = new JobScheduler();
            var handles = new List<JobHandle>(100);

            for (var index = 0; index < 40; index++)
            {
                var job = new CalculationJob(index, index);
                var handle = jobScheduler.Schedule(job);
                handles.Add(handle);
                Console.WriteLine($"Handle {index} scheduled");
            }

            jobScheduler.Flush(handles.AsSpan());
            jobScheduler.Wait(handles.AsSpan());
            Console.WriteLine($"{sindex} done");
            jobScheduler.Dispose();
        }*/

        //using var jobScheduler = new JobScheduler();

        //Spawn massive jobs and wait for finish
        // for (var index = 0; index < 1000; index++)
        // {
        //     var indexCopy = index;
        //     var job = new TestJob(index, () => { Console.WriteLine($"FINISHED {indexCopy}"); });
        //
        //     var handle1 = jobScheduler.Schedule(job);
        //     jobScheduler.Flush(handle1);
        // }
        //
        // Thread.Sleep(10_000);


        /*var handles = new JobHandle[180];
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
        BenchmarkSwitcher.FromAssembly(typeof(Benchmark).Assembly).Run(args);
    }
}
