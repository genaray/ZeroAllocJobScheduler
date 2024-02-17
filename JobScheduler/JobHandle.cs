using System.Runtime.CompilerServices;

namespace Schedulers;

/// <summary>
///     The <see cref="JobHandle"/> struct
///     is used to control and await a scheduled <see cref="IJob"/>.
/// </summary>
public readonly struct JobHandle : IEquatable<JobHandle>
{
    /// <summary>
    ///     Assigns schedulers an ID, and a cache of tracked jobs.
    ///     This way, we can store a Scheduler and a Job on a JobHandle by integer ID,
    ///     so that stackalloc JobHandle[] can work. Otherwise the managed types would prevent it.
    /// </summary>
    // A dictionary is OK because we only add when we initialize a new scheduler.
    // It otherwise doesn't use memory.
    // Accesses could be sliiiightly faster if we used an array and recycled IDs but not appreciably so,
    // particularly for the overhead it would involve.
    private static readonly Dictionary<int, (JobScheduler Scheduler, Job[] JobIds)>
        _schedulerCache = [];

    /// <summary>
    ///     Initialize a new Scheduler with the handle-recycling system. Will spontaneously allocate.
    /// </summary>
    /// <param name="schedulerId">
    ///     The ID of the scheduler. Must be unique per scheduler instance, and must never
    ///     be recycled.
    /// </param>
    /// <param name="scheduler">The scheduler object.</param>
    /// <param name="jobsCount">The number of jobs to </param>
    internal static void InitializeScheduler(int schedulerId, JobScheduler scheduler, int jobsCount)
    {
        lock (_schedulerCache)
        {
            _schedulerCache[schedulerId] = (scheduler, new Job[jobsCount]);
        }
    }

    /// <summary>
    ///     Track a newly-created job with the handle-recycling system. Will spontaneously allocate.
    /// </summary>
    /// <param name="schedulerId">The ID of the scheduler to track jobs for.</param>
    /// <param name="job">The job object to track.</param>
    internal static void TrackJob(int schedulerId, Job job)
    {
        lock (_schedulerCache)
        {
            var cache = _schedulerCache[schedulerId];
            if (job.InstanceId >= cache.JobIds.Length)
            {
                Array.Resize(ref cache.JobIds, cache.JobIds.Length * 2);
                _schedulerCache[schedulerId] = cache;
            }

            cache.JobIds[job.InstanceId] = job;
        }
    }

    /// <summary>
    ///     Remove a scheduler, and all tracked job IDs.
    ///     This will invalidate all existing handles; any methods on them will be invalid.
    /// </summary>
    /// <param name="id"></param>
    internal static void DisposeScheduler(int id)
    {
        lock (_schedulerCache)
        {
            _schedulerCache.Remove(id);
        }
    }

    /// <summary>
    ///     Creates a new <see cref="JobHandle"/> instance.
    /// </summary>
    /// <param name="schedulerId">The <see cref="JobScheduler"/> instance ID.</param>
    /// <param name="version">The current version of the job.</param>
    /// <param name="jobId">The job to assciate with this handle.</param>
    internal JobHandle(int schedulerId, int version, long jobId)
    {
        Version = version;
        SchedulerId = schedulerId;
        JobId = jobId;
    }

    /// <summary>
    ///     The <see cref="JobScheduler"/> used by this scheduled job, as tracked by the ID system.
    /// </summary>
    internal int SchedulerId { get; }

    /// <summary>
    ///     The <see cref="Job"/> that was associated with the handle on creation, as tracked by the
    ///     ID system.
    ///     May not be the current job, if the version is expired.
    /// </summary>
    internal long JobId { get; }

    /// <summary>
    ///     The job version used by this scheduled job. If this doesn't match <see cref="Job"/>, it means
    ///     the job is completed and the original object was recycled.
    /// </summary>
    internal int Version { get; }

    internal JobScheduler Scheduler
    {
        get
        {
            lock (_schedulerCache)
            {
                if (!_schedulerCache.TryGetValue(SchedulerId, out var foundScheduler))
                {
                    throw new InvalidOperationException($"Cannot process a job handle from a disposed scheduler!");
                }

                return foundScheduler.Scheduler;
            }
        }
    }

    internal Job Job
    {
        get
        {
            lock (_schedulerCache)
            {
                if (!_schedulerCache.TryGetValue(SchedulerId, out var foundScheduler))
                {
                    throw new InvalidOperationException($"Cannot process a job handle from a disposed scheduler!");
                }

                var jobIds = foundScheduler.JobIds;

                if (JobId >= jobIds.Length)
                {
                    throw new InvalidOperationException($"Job ID not tracked!");
                }

                return jobIds[JobId];
            }
        }
    }

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
        return SchedulerId == other.SchedulerId &&
               JobId == other.JobId &&
               Version == other.Version;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return HashCode.Combine(SchedulerId, JobId, Version);
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
