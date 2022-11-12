using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using JobScheduler.Extensions;
using Microsoft.Extensions.ObjectPool;

namespace JobScheduler;

/// <summary>
/// Represents a job which can outsource tasks to the <see cref="JobScheduler"/>.
/// </summary>
public interface IJob {
    
    /// <summary>
    /// Gets called by a thread to execute the job logic.
    /// </summary>
    void Execute();
    
    /// <summary>
    /// Schedules multiple jobs to the global <see cref="JobScheduler"/>. Must have been initialized before. 
    /// </summary>
    /// <param name="jobs">The jobs array</param>
    /// <param name="handles">A empty list where the handles will land in</param>
    /// <typeparam name="T">The type</typeparam>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Schedule<T>(IList<T> jobs, IList<JobHandle> handles) where T : IJob {

        for (var index = 0; index < jobs.Count; index++) {

            var handle = jobs[index].Schedule();
            handles.Add(handle);
        }
    }
}

/// <summary>
/// Pares a <see cref="JobHandle"/> with its <see cref="IJob"/>
/// </summary>
internal readonly struct JobMeta {

    public JobMeta(in JobHandle jobHandle, IJob job) {
        JobHandle = jobHandle;
        Job = job;
    }

    public JobHandle JobHandle { get; }
    public IJob Job { get; }
}

/// <summary>
/// A jobscheduler, schedules and processes <see cref="IJob"/>'s async. Better suited for larger jobs due to its underlaying events. 
/// </summary>
public class JobScheduler : IDisposable{

    /// <summary>
    /// Creates an instance and singleton.
    /// </summary>
    /// <param name="threads">The amount of worker threads to use. If zero we will use the amount of processors available.</param>
    public JobScheduler(string threadPrefix, int threads = 0) {

        Instance = this;
        ManualResetEventPool = new DefaultObjectPool<ManualResetEvent>(new ManualResetEventPolicy());
        
        var amount = threads;
        if (amount == 0) amount = Environment.ProcessorCount;

        CancellationTokenSource = new CancellationTokenSource();

        for (var index = 0; index < amount; index++) {

            var thread = new Thread(() => Loop(CancellationTokenSource.Token));
            thread.Name = $"{threadPrefix}{index}";
            thread.Start();
            
            Threads.Add(thread);
        }
    }
    
    private void Loop(CancellationToken token) {

        while (!token.IsCancellationRequested) {

            Event.WaitOne();
            Event.Reset();

            while (Jobs.TryDequeue(out var jobMeta)) {

                jobMeta.Job.Execute();
                jobMeta.JobHandle.Notify();
                
                if(jobMeta.JobHandle._poolOnComplete)
                    ManualResetEventPool.Return(jobMeta.JobHandle._event);
            }
        }
    }
    
    /// <summary>
    /// Schedules a job. Is only queued up, not being processed. 
    /// </summary>
    /// <param name="job">The job to process</param>
    /// <param name="poolOnComplete">Is set, the worker thread will return the handle to the pool after on. The user should not call Return oder Complete on it !</param>
    /// <returns>Its <see cref="JobHandle"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public JobHandle Schedule(IJob job, bool poolOnComplete = false) {

        var handle = new JobHandle(ManualResetEventPool.Get(), poolOnComplete);
        handle._event.Reset();

        var jobMeta = new JobMeta(in handle, job);
        QueuedJobs.Enqueue(jobMeta);
        return handle;
    }

    /// <summary>
    /// Flushes all queued <see cref="IJob"/>'s to the worker threads. 
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Flush() {
        
        while (QueuedJobs.TryDequeue(out var jobMeta)) {
            Jobs.Enqueue(jobMeta);
        }
        Event.Set();
    }

    /// <summary>
    /// Global instance. 
    /// </summary>
    public static JobScheduler Instance { get; set; }

    internal CancellationTokenSource CancellationTokenSource;
    internal ManualResetEvent Event = new ManualResetEvent(false);
    internal List<Thread> Threads { get; set; } = new List<Thread>();

    internal Queue<JobMeta> QueuedJobs = new Queue<JobMeta>();
    internal ConcurrentQueue<JobMeta> Jobs = new ConcurrentQueue<JobMeta>();

    internal static DefaultObjectPool<ManualResetEvent> ManualResetEventPool;

    /// <summary>
    /// Disposes all internals. 
    /// </summary>
    public void Dispose() {

        CancellationTokenSource.Cancel(false);
        Event.Dispose();
        
        QueuedJobs.Clear();
        Jobs.Clear();
    }
}