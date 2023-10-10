using System.Runtime.CompilerServices;

namespace JobScheduler;

/// <summary>
///     The <see cref="JobId"/> struct
///     uniquely identifies a particular run job.
/// </summary>
internal record struct JobId
{
    /// <summary>
    ///     Creates an instance of the <see cref="JobId"/>.
    /// </summary>
    /// <param name="id">Its id.</param>
    /// <param name="version">Its version.</param>
    public JobId(int id, int version)
    {
        Id = id;
        Version = version;
    }
    
    /// <summary>
    ///     The id of the job.
    /// </summary>
    public int Id { get; }
    
    /// <summary>
    ///     Its version.
    /// <remarks>Since each <see cref="JobId"/> is being recycled, it has this version that gets incremented each time it was recycled.</remarks>
    /// </summary>
    public int Version { get; internal set; }
}

/// <summary>
///     The <see cref="JobHandle"/> struct
///     is used to control and await a scheduled <see cref="IJob"/>.
/// </summary>
public readonly struct JobHandle
{

    /// <summary>
    ///     Creates a new <see cref="JobHandle"/> instance.
    /// </summary>
    /// <param name="scheduler">The <see cref="JobScheduler"/>.</param>
    /// <param name="id">Its <see cref="JobId"/>.</param>
    internal JobHandle(JobScheduler scheduler, JobId id)
    {
        JobId = id;
        Scheduler = scheduler;
    }
    
    /// <summary>
    ///     The <see cref="JobScheduler"/> used by this scheduled job.
    /// </summary>
    internal JobScheduler Scheduler { get; }
    
    /// <summary>
    ///     The <see cref="JobId"/> used by this scheduled job.
    /// </summary>
    internal JobId JobId { get; }

    /// <summary>
    /// Waits for the <see cref="JobHandle"/> to complete.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Complete()
    {
        Scheduler.Complete(JobId);
    }

    /// <summary>
    /// Waits and blocks the calling thread until all <see cref="JobHandle"/>s are completed.
    /// </summary>
    /// <remarks>This is equivalent to calling <see cref="Complete()"/> on each <see cref="JobHandle"/> individually.</remarks>
    /// <param name="handles">The handles to complete.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void CompleteAll(JobHandle[] handles)
    {
        foreach (var handle in handles) handle.Complete();
    }

    /// <summary>
    /// Waits and blocks the calling thread until all <see cref="JobHandle"/>s are completed.
    /// </summary>
    /// <remarks>This is equivalent to calling <see cref="Complete()"/> on each <see cref="JobHandle"/> individually.</remarks>
    /// <param name="handles">The handles to complete.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void CompleteAll(IList<JobHandle> handles)
    {
        foreach (var handle in handles) handle.Complete();
    }
}
