using System.Runtime.CompilerServices;

namespace JobScheduler;

/// <summary>
///     The <see cref="JobHandle"/> struct
///     is used to control and await a scheduled <see cref="IJob"/>.
/// </summary>
public readonly struct JobHandle : IEquatable<JobHandle>
{
    /// <summary>
    ///     Creates a new <see cref="JobHandle"/> instance.
    /// </summary>
    /// <param name="scheduler">The <see cref="JobScheduler"/>.</param>
    /// <param name="version">The current version of the job.</param>
    /// <param name="job">The job to assciate with this handle.</param>
    internal JobHandle(JobScheduler scheduler, int version, Job job)
    {
        Version = version;
        Scheduler = scheduler;
        Job = job;
    }

    /// <summary>
    ///     The <see cref="JobScheduler"/> used by this scheduled job.
    /// </summary>
    internal JobScheduler Scheduler { get; }

    /// <summary>
    ///     The <see cref="Job"/> that was associated with the handle on creation.
    ///     May not be the current job, if the version is expired.
    /// </summary>
    internal Job Job { get; }

    /// <summary>
    ///     The job version used by this scheduled job. If this doesn't match <see cref="Job"/>, it means
    ///     the job is completed.
    /// </summary>
    internal int Version { get; }

    /// <summary>
    ///     Waits for the <see cref="JobHandle"/> to complete.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Complete()
    {
        Scheduler.Complete(this);
    }

    /// <summary>
    ///     Waits and blocks the calling thread until all <see cref="JobHandle"/>s are completed.
    /// </summary>
    /// <remarks>
    ///     This is equivalent to calling <see cref="Complete()"/> on each <see cref="JobHandle"/> individually.
    /// </remarks>
    /// <param name="handles">The handles to complete.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void CompleteAll(ReadOnlySpan<JobHandle> handles)
    {
        foreach (var handle in handles)
        {
            handle.Complete();
        }
    }

    /// <inheritdoc cref="CompleteAll(ReadOnlySpan{JobHandle})"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void CompleteAll(IReadOnlyList<JobHandle> handles)
    {
        for (var i = 0; i < handles.Count; i++)
        {
            handles[i].Complete();
        }
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        return obj is JobHandle handle && Equals(handle);
    }

    /// <inheritdoc/>
    public bool Equals(JobHandle other)
    {
        return EqualityComparer<JobScheduler>.Default.Equals(Scheduler, other.Scheduler) &&
               EqualityComparer<Job>.Default.Equals(Job, other.Job) &&
               Version == other.Version;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return HashCode.Combine(Scheduler, Job, Version);
    }

    /// <inheritdoc/>
    public static bool operator ==(JobHandle left, JobHandle right)
    {
        return left.Equals(right);
    }

    /// <inheritdoc/>
    public static bool operator !=(JobHandle left, JobHandle right)
    {
        return !(left == right);
    }
}
