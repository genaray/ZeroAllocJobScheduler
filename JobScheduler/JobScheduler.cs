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
        }

        foreach (var worker in Workers)
        {
            worker.Start();
        }
    }

    /// <summary>
    /// A list of all <see cref="WorkStealingDeque{T}"/> of the Workers with <see cref="IJob"/>s that are currently being processed.
    /// </summary>
    internal List<WorkStealingDeque<JobHandle>> Queues { get; } = new();

    /// <summary>
    /// All active <see cref="Workers"/>.
    /// </summary>
    internal List<Worker> Workers { get; } = new();

    /// <summary>
    /// An index pointing towards the next worker being used to process the next flushed <see cref="IJob"/>.
    /// </summary>
    internal int NextWorkerIndex { get; set; }

    /// <summary>
    /// Creates a new <see cref="JobHandle"/> from a <see cref="IJob"/>.
    /// </summary>
    /// <param name="iJob">The <see cref="IJob"/>.</param>
    /// <param name="pooled">Whether the handle should be pooled or not</param>
    /// <returns>The new created <see cref="JobHandle"/>.</returns>
    public JobHandle Schedule(IJob iJob)
    {
        return JobHandle.Pool.GetNewHandle(iJob);
    }


    /// <summary>
    /// This is similar to wait but doesn't require a job handle, and allow limiting execution to a certain amount of jobs.
    /// </summary>
    /// <param name="max">The maximum amount of jobs that will be executed, 0 means unlimited</param>
    public void TryToExecuteRemainingJobs(int max = 0)
    {
        var totalExecuted = 0;
        for (var i = 0; i < Workers.Count; i++)
        {
            var nextJob = Workers[i].Queue.TrySteal(out var stolenJob);
            if (!nextJob)
            {
                continue;
            }

            stolenJob.Job.Execute();
            Finish(stolenJob);
            totalExecuted++;
            if (max > 0 && totalExecuted >= max)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Creates a new <see cref="JobHandle"/> from a <see cref="IJob"/> with a <see cref="JobHandle"/> as a parent.
    /// Links the job to the parent. This allows you to wait for the parent until all of its children have been processed.
    /// </summary>
    /// <param name="iJob">The <see cref="IJob"/>.</param>
    /// <param name="parent">The parent <see cref="JobHandle"/>.</param>
    /// <returns>The new <see cref="JobHandle"/>.</returns>
    public JobHandle Schedule(IJob iJob, JobHandle parent)
    {
        Interlocked.Increment(ref parent.UnfinishedJobs);
        var job = JobHandle.Pool.GetNewHandle(iJob);
        job.Parent = parent.Index;
        return job;
    }

    /// <summary>
    /// Creates a dependency between two <see cref="JobHandle"/>s.
    /// The dependent <see cref="JobHandle"/>s is executed after the target <see cref="JobHandle"/> has finished.
    /// This ensures that a <see cref="JobHandle"/> is executed at a later point in time.
    /// </summary>
    /// <param name="dependency">The <see cref="JobHandle"/> dependency.</param>
    /// <param name="dependOn">The <see cref="JobHandle"/> it depends on.</param>
    public void AddDependency(JobHandle dependency, JobHandle dependOn)
    {
        dependOn.GetDependencies().Add(dependency);
    }


    /// <summary>
    /// Transfers a <see cref="JobHandle"/> to the <see cref="Workers"/> so that it can be executed.
    /// </summary>
    /// <param name="job">The <see cref="JobHandle"/>.</param>
    public void Flush(JobHandle job)
    {
        // Round Robin,
        var workerIndex = NextWorkerIndex;
        while (!Workers[workerIndex].IncomingQueue.TryEnqueue(job))
        {
            NextWorkerIndex = (NextWorkerIndex + 1) % Workers.Count;
        }

        NextWorkerIndex = (NextWorkerIndex + 1) % Workers.Count;
    }

    /// <summary>
    /// Wait until a <see cref="JobHandle"/> and all its children have been completed.
    /// Also works on jobs in the meantime.
    /// </summary>
    /// <param name="job">The <see cref="JobHandle"/>.</param>
    public void Wait(JobHandle job)
    {
        while (job.UnfinishedJobs > 0)
        {
            for (var i = 0; i < Workers.Count; i++)
            {
                var nextJob = Workers[i].Queue.TrySteal(out var stolenJob);
                if (!nextJob)
                {
                    continue;
                }

                stolenJob.Job.Execute();
                Finish(stolenJob);
            }
        }
    }

    /// <summary>
    /// Completely ends a <see cref="JobHandle"/> and his children by cleaning it up.
    /// Transfers dependencies and has them executed.
    /// </summary>
    /// <param name="job"></param>
    internal void Finish(JobHandle job)
    {
        var unfinishedJobs = Interlocked.Decrement(ref job.UnfinishedJobs);
        if (unfinishedJobs != 0)
        {
            return;
        }

        if (job.Parent != ushort.MaxValue)
        {
            Finish(new(job.Parent));
        }

        if (job.HasDependencies())
        {
            for (var index = 0; index < job.GetDependencies().Count; index++)
            {
                var nextJob = job.GetDependencies()[index];
                Flush(nextJob);
            }
        }

        if(job.UnfinishedJobs <0) throw new InvalidOperationException("Unfinished jobs cannot be negative");
        JobHandle.Pool.ReleaseHandle(job);
    }

    /// <summary>
    /// Cleans this instance and terminates all <see cref="Worker"/>s.
    /// </summary>
    public void Dispose()
    {
        foreach (var worker in Workers)
        {
            worker.Stop();
        }
    }
}
