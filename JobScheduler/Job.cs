using System;
using System.Diagnostics;
using JobScheduler.Deque;

namespace JobScheduler;

/// <summary>
///     The <see cref="Job"/> struct
///     represents the Job itself with all its most important data. 
/// </summary>
internal class Job
{
    private readonly static XorshiftRandom _random = new();
    // the scheduler this job was created with
    private readonly JobScheduler _scheduler;

    // The version of this job. Many methods must have a version passed in. If that doesn't match ours,
    // it means the job is already complete.
    private int _version = 0;

    // The actual code of the job.
    private IJob? _work;

    #region ParallelFields

    // If this actually has parallel work instead, this is that work
    private IJobParallelFor? _parallelWork;

    // If we're handling a parallel job, the master job manages all the deques and lifecycle.
    private JobHandle? _masterJob;

    // If we're handling a parallel job, this is our temporary ID in the system.
    private int _parallelJobID;

    // If we're the master job, we need to track who still needs access to our properties.
    // This is volatile because we need out-of-lock access to spin while we wait for subscribers to catch up.
    private volatile int _parallelSubscribers;

    // The master job stores one of these for each thread.
    // Note that due to pooling, every job stores some of these. They should be pretty cheap though, and we're
    // already doing worse memory crimes.
    private readonly RangeWorkStealingDeque[] _workerDeques;

    #endregion

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
    /// <param name="threadCapacity"></param>
    /// <param name="scheduler">The scheduler this <see cref="Job"/> was created with.</param>
    public Job(int dependentCapacity, int threadCapacity, JobScheduler scheduler)
    {
        _waitHandle = new(false);
        _scheduler = scheduler;
        _dependents = new(dependentCapacity);
        _workerDeques = new RangeWorkStealingDeque[threadCapacity];
        for (int i = 0; i < threadCapacity; i++)
        {
            _workerDeques[i] = new();
        }

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
    /// <param name="totalThreads"></param>
    /// <param name="thisThread"></param>
    /// <returns></returns>
    public JobHandle Schedule(IJob? work, List<JobHandle> dependencies, out bool ready,
        IJobParallelFor? parallelWork = null, JobHandle? masterJob = null, int amount = 0, int totalThreads = 0, int thisThread = 0)
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
                _parallelJobID = thisThread;
                // if masterJob is null, we set it to our own handle later.
                if (masterJob is null)
                {
                    Debug.Assert(thisThread == 0);

                    // split into batches equally into the worker deques
                    // if there are extra deques left afterwards, it's OK, because on pool we made sure they were all empty.
                    var baseAmount = amount / totalThreads;
                    var remainder = amount % totalThreads;
                    var start = 0;

                    // just stick the remainder in the first bucket
                    _workerDeques[0].Set(start, baseAmount + remainder, _parallelWork.BatchSize);
                    start += baseAmount + remainder;
                    for (var i = 1; i < totalThreads; i++)
                    {
                        var end = start + baseAmount;
                        _workerDeques[i].Set(start, baseAmount, _parallelWork.BatchSize);
                        start = end;
                    }
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
                var workerDeques = _masterJob.Value.Job._workerDeques;
                while (true)
                {
                    // process a single batch from our own queue, until we fail
                    if (workerDeques[_parallelJobID].TryPopBottom(out var range)
                        != RangeWorkStealingDeque.Status.Success)
                    {
                        break;
                    }

                    for (var i = range.Start.Value; i < range.End.Value; i++)
                    {
                        _parallelWork.Execute(i);
                    }
                }

                // start work stealing
                // this isn't perfect, and could likely be improved a bit with a more Lin et al approach.
                // But a full Lin et al approach is irrelevant because things can't be added to deques.
                // So this random search method works OK for now.
                // (the main takeaway from lin et al here is to track num_thieves and num_actives?)
                while (true)
                {
                    RangeWorkStealingDeque? victim = null;

                    // start at a random place in the list, and find a victim from there
                    var start = _random.Next(0, workerDeques.Length);
                    var i = start;
                    do
                    {
                        var vic = workerDeques[i];
                        if (!vic.IsEmpty)
                        {
                            victim = vic;
                            break;
                        }

                        i++;
                        if (i >= workerDeques.Length)
                        {
                            i = 0;
                        }
                    }
                    while (i != start);

                    // We've confirmed all deques are empty, we're done
                    if (victim is null)
                    {
                        break;
                    }

                    // Commence the steal!
                    // This will just try again and again in the case of a contention, which may waste power.
                    // But since parallel fors are generally short, we want to dedicate resources to them.
                    // This is equivalent to an infinite STEAL_BOUND in Lin et al.
                    while (victim.TrySteal(out var range) != RangeWorkStealingDeque.Status.Empty)
                    {
                        for (var r = range.Start.Value; r < range.End.Value; r++)
                        {
                            _parallelWork.Execute(r);
                        }
                    }
                }

                _masterJob.Value.Job.UnsubscribeFromParallel(_masterJob.Value.Version);
#if DEBUG
                foreach (var deque in workerDeques)
                {
                    Debug.Assert(deque.IsEmpty);
                }
#endif
            }

            // If we were the master job, and someone is still waiting, we're not allowed to continue until they've unsubscribed.
            // Since we know at this point that we're done with all the deques, we just spin until all the subscribers have finished
            // their batches. Then we're allowed to clean up. In the meantime, nobody can possibly resubscribe because the deques are
            // empty.
            if (_masterJob.Value.Job == this && _parallelSubscribers > 0)
            {
                var spin = new SpinWait();
#if DEBUG
                foreach (var deque in _workerDeques)
                {
                    Debug.Assert(deque.IsEmpty);
                }
#endif

                // We're positive that _parallelSubscribers can never increase at this point, because the deques are empty.
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
            if (_version != version || _isComplete)
            {
                return false;
            }

            // check to see if there are any empty valid deques before we allow subscribing
            var foundDeque = false;
            foreach (var deque in _workerDeques)
            {
                if (!deque.IsEmpty)
                {
                    foundDeque = true;
                }
            }

            if (!foundDeque)
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
        _parallelSubscribers = 0;
#if DEBUG
        foreach (var deque in _workerDeques)
        {
            Debug.Assert(deque.IsEmpty);
        }  
#endif
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
