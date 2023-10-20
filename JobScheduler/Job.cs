using System.Diagnostics;

namespace JobScheduler;

/// <summary>
///     The <see cref="Job"/> struct
///     represents the Job itself with all its most important data. 
/// </summary>
internal class Job
{
    // the scheduler this job was created with
    private readonly JobScheduler _scheduler;

    // The version of this job. Many methods must have a version passed in. If that doesn't match ours,
    // it means the job is already complete.
    private int _version = 0;

    // The actual code of the job.
    private IJob? _work;

    private IJobParallelFor? _parallelWork;
    private JobHandle? _masterJob;
    private int _parallelWorkIndex;
    private int _totalParallelWork;
    private volatile int _parallelSubscribers;

    // The Handle of the job. 
    private readonly ManualResetEvent _waitHandle;

    // The list of dependents this job has (NOT dependencies!)
    // When this job completes it will decrease the <see cref="DependencyCount"/> of any dependents.
    private readonly List<Job> _dependents;

    // The number of Dependencies (NOT dependants!) that must complete before this job can be added to the queue
    private int _dependencyCount = 0;

    // When this hits 0, we can dispose the WaitHandle, and send ourselves back to the pool.
    private int _waitHandleSubscriptionCount = 0;

    // Whether this job is complete (i.e. needs to still be around while we wait for other threads to finish their
    // Complete() before we can pool).
    private bool _isComplete = false;

    // The per-job lock for each job instance.
    private readonly object _jobLock = new();

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
    /// <param name="parallelWork"></param>
    /// <param name="masterJob"></param>
    /// <param name="amount"></param>
    /// <returns></returns>
    public JobHandle Schedule(IJob? work, List<JobHandle> dependencies, out bool ready, IJobParallelFor? parallelWork = null, JobHandle? masterJob = null, int amount = 0)
    {
        lock (_jobLock)
        {
            // Special parallel scheduling
            if (parallelWork is not null)
            {
                if (work is not null)
                {
                    throw new ArgumentOutOfRangeException(nameof(amount));
                }

                if (amount <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(amount));
                }

                _masterJob = masterJob;
                _parallelWork = parallelWork;
                // if masterJob is null, we set it to our own handle later.
                if (masterJob is null)
                {
                    _parallelWorkIndex = -1;
                    _totalParallelWork = amount;
                }
            }

            // At this point, nobody should be decreasing our dependency count, since we just came from the pool
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

            JobHandle thisHandle = new(_scheduler, _version, this);

            if (_parallelWork is not null && _masterJob is null)
            {
                _masterJob = thisHandle;
            }

            return thisHandle;
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

        // Special parallel execution
        if (_parallelWork is not null)
        {
            Debug.Assert(_masterJob is not null);
            Debug.Assert(_work is null);

            // RACE CONDITION: If _masterJob decrements and kills itself, how do we confirm it's complete?
            // The fix is to use the TrySubscribe pattern, and Unsubscribe once we're done. Then, we ensure the master
            // job cannot kill itself until all subscribers have finished!
            // Also, If this is ourself, this will run fine.
            if (_masterJob.Value.Job.TrySubscribeToParallel(_masterJob.Value.Version))
            {
                while (true)
                {
                    // TODO: This is the slowest part about this; it's the naive solution.
                    // It should be replaced by a batched CAS with work-stealing.
                    // That way we would only need to CAS when we steal once every batch!
                    // I'm pretty sure a "fake work stealing deque" could be created with a Chase-Lev deque that tracks an int range
                    // instead of actual elements.
                    // However, the difficult part is the actual work-stealing algorithm: I don't think Lin et al. is the correct
                    // route here, because parallel jobs generally run fast enough that thereads won't be contending enough to justify
                    // sleep. So I think a simplified version is the way to go.
                    var val = Interlocked.Increment(ref _masterJob.Value.Job._parallelWorkIndex);
                    if (val >= _masterJob.Value.Job._totalParallelWork)
                    {
                        break;
                    }

                    _parallelWork.Execute(val);
                }

                _masterJob.Value.Job.UnsubscribeFromParallel(_masterJob.Value.Version);
            }

            // If we were the master job, and someone is still waiting, we're not allowed to continue until they've unsubscribed.
            // Since we know at this point that we're done with all/most of the execution (we have a max 1 execution per thread left)
            // we just spin until they make it.
            if (_masterJob.Value.Job == this && _parallelSubscribers > 0)
            {
                var spin = new SpinWait();

                // We're positive that _parallelSubscribers can never increase at this point, because _parallelWorkLeft is 0 or less.
                // (all TrySubscribeToParallel will fail).
                // So we don't have to lock while checking.
                // And because _parallelSubscribers is volatile, there won't be any non-atomic nonsense with the decrementing.
                while (_parallelSubscribers > 0)
                {
                    spin.SpinOnce();
                }
            }
        }

        // You may think that we have to manage parallel dependencies here, but actually we don't!
        // The handle is for the master job, always. And if we're the master job, we're about to resolve those.
        // If we're not the master job, then we won't have any dependents, and we'll mark ourselves as complete and exit.
        // The master job has the exact same dependenc*ies* as all the rest of us, so it'll execute via work-stealing
        // very very soon and complete its handle as necessary, firing any dependents.

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

    private void UnsubscribeFromParallel(int version)
    {
        lock (_jobLock)
        {
            Debug.Assert(version == _version);
            _parallelSubscribers--;
        }
    }

    private bool TrySubscribeToParallel(int version)
    {
        lock (_jobLock)
        {
            if (_version != version || _isComplete || _parallelWorkIndex >= _totalParallelWork)
            {
                return false;
            }

            _parallelSubscribers++;
            return true;
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
        _parallelWork = null;
        _parallelWorkIndex = 0;
        _parallelSubscribers = 0;
        _totalParallelWork = 0;
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
