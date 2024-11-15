using Schedulers;
using Schedulers.Utils;

namespace Arch.Benchmarks;

public class TestJob : IJob
{
    private readonly int _id;
    private readonly Action _action;

    public TestJob(int id, Action action = null)
    {
        _id = id;
        _action = action;
    }

    public void Execute()
    {
        _action?.Invoke();
    }
}


public class Benchmark
{
    private static void Main(string[] args)
    {

        using var jobScheduler = new JobScheduler();

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

        var handles = new JobHandle[18];
        for (var index = 0; index < 18; index++)
        {
            var indexCopy = index;
            var job = new TestJob(index, () =>
            {
                Thread.Sleep(1000);
                Console.WriteLine($"FINISHED {indexCopy}");
            });

            var handle = jobScheduler.Schedule(job);
            handles[index] = handle;
        }

        jobScheduler.Flush(handles);
        jobScheduler.Wait(handles);
        Console.WriteLine("Finished");
        // Use: dotnet run -c Release --framework net7.0 -- --job short --filter *BenchmarkClass1*
        //BenchmarkSwitcher.FromAssembly(typeof(Benchmark).Assembly).Run(args);
    }
}
