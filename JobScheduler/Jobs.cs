using JobScheduler.Extensions;
using Microsoft.Extensions.ObjectPool;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace JobScheduler;

/// <summary>
/// Represents a job which can outsource tasks to the <see cref="JobScheduler"/>.
/// </summary>
public interface IJob
{
    /// <summary>
    /// Gets called by a thread to execute the job logic.
    /// </summary>
    void Execute();

    /// <summary>
    /// Schedules multiple jobs to the global <see cref="JobScheduler"/>. Must have been initialized before. 
    /// </summary>
    /// <param name="jobs">The jobs array</param>
    /// <param name="handles">A list that will be cleared and filled with the <see cref="JobHandle"/>s of the scheduled jobs.
    /// The caller should cache and reuse the list.</param>
    /// <typeparam name="T">The job type</typeparam>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Schedule<T>(IList<T> jobs, IList<JobHandle> handles) where T : IJob
    {
        handles.Clear();
        for (var index = 0; index < jobs.Count; index++)
        {
            var handle = jobs[index].Schedule();
            handles.Add(handle);
        }
    }
}

/// <summary>
/// Pairs a <see cref="JobHandle"/> with its <see cref="IJob"/>
/// </summary>
internal readonly struct JobMeta
{
    public JobMeta(in JobHandle jobHandle, IJob job)
    {
        JobHandle = jobHandle;
        Job = job;
    }

    public JobHandle JobHandle { get; }
    public IJob Job { get; }
}

/// <summary>
/// A <see cref="JobScheduler"/> schedules and processes <see cref="IJob"/>ss asynchronously. Better-suited for larger jobs due to its underlying events. 
/// </summary>
/// <remarks>On the first construction, this class will create a global singleton, accessible via <see cref="Instance"/>.</remarks>
public class JobScheduler : IDisposable
{
    /// <summary>
    /// Creates an instance and singleton.
    /// </summary>
    /// <param name="threadPrefix">The thread prefix to use. The thread will be named "prefix0" for the first thread, "prefix1" for the second thread, etc.</param>
    /// <param name="threads">The amount of worker threads to use. If zero we will use the amount of processors available.</param>
    public JobScheduler(string threadPrefix, int threads = 0)
    {
        Instance = this;
        ManualResetEventPool = new DefaultObjectPool<ManualResetEvent>(new ManualResetEventPolicy());

        var amount = threads;
        if (amount == 0) amount = Environment.ProcessorCount;

        CancellationTokenSource = new CancellationTokenSource();

        for (var index = 0; index < amount; index++)
        {
            var thread = new Thread(() => Loop(CancellationTokenSource.Token))
            {
                Name = $"{threadPrefix}{index}"
            };
            thread.Start();

            Threads.Add(thread);
        }
    }

    private void Loop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            Event.WaitOne();
            Event.Reset();

            while (Jobs.TryDequeue(out var jobMeta))
            {
                jobMeta.Job.Execute();
                jobMeta.JobHandle.Notify();

                if (jobMeta.JobHandle._poolOnComplete)
                    ManualResetEventPool.Return(jobMeta.JobHandle._event);
            }
        }
    }

    /// <summary>
    /// Schedules a job. It is only queued up, and will only begin processing when the user calls <see cref="Flush()"/>.
    /// </summary>
    /// <param name="job">The job to process</param>
    /// <param name="poolOnComplete">If set, the worker thread will automatically return the handle to the pool after it completes. 
    /// The user should not call <see cref="JobHandle.Return()"/> or <see cref="JobHandle.Complete()"/> on it!</param>
    /// <returns>Its <see cref="JobHandle"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public JobHandle Schedule(IJob job, bool poolOnComplete = false)
    {
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
    public void Flush()
    {
        while (QueuedJobs.TryDequeue(out var jobMeta))
        {
            Jobs.Enqueue(jobMeta);
        }
        Event.Set();
    }

    private static JobScheduler? _instance;

    /// <summary>
    /// Global instance. 
    /// </summary>
    public static JobScheduler Instance
    {
        get => _instance ?? throw new InvalidOperationException($"Cannot access {nameof(Instance)} before initialization; construct an instance of {nameof(JobScheduler)} first!");
        private set => _instance = value;
    }

    internal CancellationTokenSource CancellationTokenSource;
    internal ManualResetEvent Event = new(false);
    internal List<Thread> Threads { get; set; } = new List<Thread>();

    internal Queue<JobMeta> QueuedJobs = new();
    internal ConcurrentQueue<JobMeta> Jobs = new();

    private static DefaultObjectPool<ManualResetEvent>? _manualResetEventPool;
    internal static DefaultObjectPool<ManualResetEvent> ManualResetEventPool
    {
        get => _manualResetEventPool ?? throw new InvalidOperationException($"Cannot access {nameof(Instance)} before initialization; construct an instance of {nameof(JobScheduler)} first!");
        private set => _manualResetEventPool = value;
    }

    /// <summary>
    /// Disposes all internals. 
    /// </summary>
    public void Dispose()
    {
        CancellationTokenSource.Cancel(false);
        Event.Dispose();

        QueuedJobs.Clear();
        Jobs.Clear();
    }
}