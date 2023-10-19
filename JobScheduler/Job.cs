using Microsoft.Extensions.ObjectPool;
using System.Diagnostics;

namespace JobScheduler;

/// <summary>
///     The <see cref="Job"/> struct
///     represents the Job itself with all its most important data. 
/// </summary>
internal class Job
{
    // the scheduler this job was created with
    readonly JobScheduler _scheduler;

    // The version of this job. Many methods must have a version passed in. If that doesn't match ours,
    // it means the job is already complete.
    int _version = 0;

    // The actual code of the job.
    IJob? _work;

    // The Handle of the job. 
    readonly ManualResetEvent _waitHandle;

    // The list of dependents this job has (NOT dependencies!)
    // When this job completes it will decrease the <see cref="DependencyCount"/> of any dependents.
    readonly List<Job> _dependents;

    // The number of Dependencies (NOT dependants!) that must complete before this job can be added to the queue
    int _dependencyCount = 0;

    // When this hits 0, we can dispose the WaitHandle, and send ourselves back to the pool.
    int _waitHandleSubscriptionCount = 0;

    // Whether this job is complete (i.e. needs to still be around while we wait for other threads to finish their
    // Complete() before we can pool).
    bool _isComplete = false;

    // The per-job lock for each job instance.
    readonly object _jobLock = new();

    /// <summary>
    /// Create a new <see cref="Job"/> with dependent capacity <paramref name="dependentCapacity"/>, ready for scheduling.
    /// Automatically adds itself to the <see cref="JobScheduler"/>'s pool, meaning you should acquire it only from the pool
    /// and never use a <see cref="Job"/> straight from construction.
    /// </summary>
    /// <param name="dependentCapacity"></param>
    /// <param name="scheduler">The scheduler this <see cref="Job"/> was created with.</param>
    public Job(int dependentCapacity, JobScheduler scheduler)
    {
        _waitHandle = new(false);
        _scheduler = scheduler;
        _dependents = new(dependentCapacity);

        // we don't need to lock because adding to the pool is the final operation of this method.
        PoolSelf();
    }

    /// <summary>
    /// Schedule a new instance of this job. It must be fresh out of the pool.
    /// </summary>
    /// <param name="work"></param>
    /// <param name="dependencies"></param>
    /// <param name="ready"></param>
    /// <returns></returns>
    public JobHandle Schedule(IJob? work, List<JobHandle> dependencies, out bool ready)
    {
        lock (_jobLock)
        {
            // at this point, nobody should be decreasing our dependency count, since we just came from the pool
            Debug.Assert(_dependencyCount == 0);

            foreach (var handle in dependencies)
            {
                // Add ourself as a dependent. We must ensure we lock on this other job's lock,
                // because if we don't, we might mess up their _dependents array!
                // Also we don't want handle.Job._isComplete to switch over after us reading it; that
                // would screw everything up.
                lock (handle.Job._jobLock)
                {
                    // exclude complete dependencies
                    if (handle.Version != handle.Job._version || handle.Job._isComplete)
                    {
                        continue;
                    }
                    handle.Job._dependents.Add(this);
                    _dependencyCount++;
                }
            }

            ready = _dependencyCount == 0; // we don't have any active dependencies
            _work = work;

            return new(_scheduler, _version, this);
        }
    }

    /// <summary>
    /// Execute the job. Fills <paramref name="readyDependents"/> with any dependents who are newly ready because of us.
    /// </summary>
    /// <param name="readyDependents"></param>
    public void Execute(List<Job> readyDependents)
    {
        // this had better be outside the lock! We don't want to block.
        _work?.Execute();
        lock (_jobLock)
        { 
            _isComplete = true;

            foreach (var dependent in _dependents)
            {
                lock (dependent._jobLock)
                {
                    // It might be tempting to use Interlocked.Decrement here to avoid locks... but don't do it!
                    // When we're adding dependents from Schedule(), we have to lock each dependent add individually.
                    // A job might not be fully scheduled yet, and if the dependent count hits 0, we get a fake ready.
                    dependent._dependencyCount--;
                    if (dependent._dependencyCount == 0)
                    {
                        readyDependents.Add(dependent);
                    }
                }
            }

            // At this point, if someone uses TrySubscribe, they'll exit because _isComplete is true
            // So we can pool outside of the lock without fear!
            if (_waitHandleSubscriptionCount != 0)
            {
                _waitHandle.Set();
            }
            else
            {
                PoolSelf();
            }
        }
    }

    // returns true if we subscribed, false if the job is already complete
    /// <summary>
    /// Prepare for a subscribe to our <see cref="ManualResetEvent"/>.
    /// Returns true if the handle is available for subscription (i.e. the job is still incomplete).
    /// If this returns true, the caller must call <see cref="Unsubscribe"/>, and may not wait on the handle
    /// after <see cref="Unsubscribe"/> is called.
    /// </summary>
    /// <param name="version"></param>
    /// <param name="handle"></param>
    /// <returns></returns>
    public bool TrySubscribe(int version, out ManualResetEvent handle)
    {
        handle = null!;
        lock (_jobLock)
        {
            if (_version != version || _isComplete)
            {
                return false;
            }

            _waitHandleSubscriptionCount++;
            handle = _waitHandle;
            return true;
        }
    }

    /// <summary>
    ///     Unsubscribe from a particular wait. Call only after <see cref="TrySubscribe"/> has returned true, and all
    ///     the handle-waiting is done.
    /// </summary>
    /// <param name="version"></param>
    public void Unsubscribe(int version)
    {
        lock (_jobLock)
        {
            Debug.Assert(version == _version);
            _waitHandleSubscriptionCount--;
            if (_waitHandleSubscriptionCount == 0)
            {
                PoolSelf();
            }
        }
    }

    private void PoolSelf()
    {
        _version++;
        _dependents.Clear();
        _dependencyCount = 0;
        _waitHandleSubscriptionCount = 0;
        _isComplete = false;
        // this may seem bad, since a Reset() wait handle so soon after a Set() handle can result in it never
        // getting called on a waiting thread, but our subscription system bypasses this by only pooling once
        // we're sure every other thread that's waiting has finished waiting.
        _waitHandle.Reset();
        _work = null;

        // we pool ourself so that it doesn't matter what thread we're on; we always are able to pool
        _scheduler.PoolJob(this);
    }
}