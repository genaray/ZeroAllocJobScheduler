using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Schedulers.Utils;

[assembly: CLSCompliant(true)]

namespace Schedulers;

/// <summary>
///     A <see cref="JobScheduler"/> schedules and processes <see cref="IJob"/>s asynchronously. Better-suited for larger jobs due to its underlying events.
/// </summary>
public partial class JobScheduler : IDisposable
{

    /// <summary>
    /// Creates an instance and singleton.
    /// </summary>
    /// <param name="threads">The amount of worker threads to use. If zero we will use the amount of processors available.</param>
    public JobScheduler(int threads = 0)
    {
        var amount = threads;
        if (amount == 0)
        {
            amount = Environment.ProcessorCount;
        }

        for (var index = 0; index < amount; index++)
        {
            var worker = new Worker(this, index);
            Workers.Add(worker);
            Queues.Add(worker.Queue);
            worker.Start();
        }
    }
    internal List<WorkStealingDeque<JobHandle>> Queues { get; } = new();
    internal List<Worker> Workers { get; } = new();

    internal int NextWorkerIndex { get; set; }

    public JobHandle Schedule(IJob function)
    {
        var job = new JobHandle(
            function
        );

        return job;
    }

    public JobHandle Schedule(IJob function, JobHandle parent)
    {
        Interlocked.Increment(ref parent._unfinishedJobs);

        var job = new JobHandle(
            function,
            parent
        );

        return job;
    }

    public void AddDependency(JobHandle dependency, JobHandle dependOn)
    {
        dependOn._dependencies.Add(dependency);
    }

    public void Flush(JobHandle job)
    {
        // Round Robin,
        var workerIndex = NextWorkerIndex;
        Workers[workerIndex].Queue.PushBottom(job);
        NextWorkerIndex = (NextWorkerIndex + 1) % Workers.Count;
    }

    public void Wait(JobHandle job)
    {
        while (job._unfinishedJobs > 0)
        {
            for (var i = 0; i < Workers.Count; i++)
            {
                var nextJob = Workers[i].Queue.TrySteal(out var stolenJob);
                if (!nextJob)
                {
                    continue;
                }

                stolenJob._job.Execute();
                Finish(stolenJob);
            }
        }
    }

    internal void Finish(JobHandle job)
    {
        var unfinishedJobs = Interlocked.Decrement(ref job._unfinishedJobs);
        if (unfinishedJobs != 0)
        {
            return;
        }

        if (job._parent != null)
        {
            Finish(job._parent);
        }

        for (var index = 0; index < job._dependencies.Count; index++)
        {
            var nextJob = job._dependencies[index];
            Flush(nextJob);
        }

        Interlocked.Decrement(ref job._unfinishedJobs);
    }

    public void Dispose()
    {
        foreach (var worker in Workers)
        {
            worker.Stop();
        }
    }
}
