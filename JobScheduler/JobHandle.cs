using System.Runtime.CompilerServices;

namespace JobScheduler;

/// <summary>
/// Used to control and await a scheduled <see cref="IJob"/>.
/// </summary>
public readonly struct JobHandle
{
    internal JobScheduler Scheduler { get; }
    internal int JobID { get; } = -1;

    internal JobHandle(JobScheduler scheduler, int id)
    {
        JobID = id;
        Scheduler = scheduler;
    }

    /// <summary>
    /// Waits for the <see cref="JobHandle"/> to complete.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Complete()
    {
        Scheduler.Complete(JobID);
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
