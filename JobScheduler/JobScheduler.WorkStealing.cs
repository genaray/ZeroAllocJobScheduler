using JobScheduler.Deque;
using System.Collections.Concurrent;

namespace JobScheduler;

// This section of JobScheduler deals with the implementation of the Lin et al. algorithm [1]. 
// It is compartmentalized into a separate partial, so that it can express the paper's algorithm as clearly and readably as possible,
// without any overhead pollution from our API. Elements that differ are marked below.
//
// The key insight of Lin et al. is their method of first attempting multiple steals, and then yielding, and then sleeping, based on
// threshold values. Other than that, it's a normal work-stealing algorithm.
//
// [1] Lin, C.-X., Huang, T.-W., & Wong, M. D. (2020). An efficient work-stealing scheduler for task dependency graph. 2020 IEEE 26th
//      International Conference on Parallel and Distributed Systems (ICPADS). https://doi.org/10.1109/icpads51040.2020.00018.
//      Retrieved October 17, 2023 from https://tsung-wei-huang.github.io/papers/icpads20.pdf
public partial class JobScheduler
{
    // The Lin et al. version uses an Eventcount, which I don't fully understand and definitely can't implement.
    // Here's the best compilation of documentation I've found on them: https://gist.github.com/mratsim/04a29bdd98d6295acda4d0677c4d0041
    // I haven't seen any .NET implementations.
    // For now, this is fine: the requirement the paper presents is that the notifier must be able to wake a single thread, or multiple threads, and wait.
    // And this class can do all those things.
    private class Notifier
    {
        // lets 1 thread through when Set(), then immediately resets
        readonly AutoResetEvent _singleNotifier = new(false);

        // lets all threads through when Set() until manually reset
        // we only use this to notify all for an exit condition
        readonly ManualResetEvent _multipleNotifier = new(false);
        readonly WaitHandle[] _both;

        public bool IsDisposed { get; private set; } = false;

        public Notifier()
        {
            _both = new WaitHandle[2];
            _both[0] = _singleNotifier;
            _both[1] = _multipleNotifier;
        }

        // block thread until a notification comes from any source
        public void Wait()
        {
            WaitHandle.WaitAny(_both);
        }

        // notify a single other thread
        public void NotifyOne()
        {
            _singleNotifier.Set();
        }

        // notify all other threads. currently permanent; can't close this gate again.
        // it's ok because we only use this to notify for an exit.
        public void NotifyAll()
        {
            // occasionally we might have an issue where we notify from the main thread when already disposed
            // this prevents that
            // this isn't a perf-effecting lock because it only happens when we're done
            lock (_disposeLock)
            {
                if (IsDisposed)
                {
                    return;
                }

                _multipleNotifier.Set();
            }
        }

        private readonly object _disposeLock = new();

        public void Dispose()
        {
            lock (_disposeLock)
            {
                if (IsDisposed)
                {
                    return;
                }

                IsDisposed = true;
                _singleNotifier.Dispose();
                _multipleNotifier.Dispose();
            }
        }
    }

    // Stores data per each worker, that only that worer has access to.
    // The only difference is when a worker steals from another's Deque.
    private class WorkerData
    {
        public WorkerData(int id, int maxJobs)
        {
            Id = id;
            ReadyDependencyCache = new(maxJobs - 1);
            Deque = new(maxJobs);
        }
        public int Id { get; }
        public Job? Cache { get; set; } = null;
        public WorkStealingDeque<Job> Deque { get; }

        // store this to cache the output from JobPool per thread
        public List<Job> ReadyDependencyCache { get; }
    }

    private int _stealBound = 0;
    private readonly int _yieldBound = 100;

    private int _numActives = 0;
    private int _numThieves = 0;

    private readonly Notifier _notifier = new();
    private readonly Random _random = new();

    private WorkerData[] _workers = null!;
    private CancellationToken _token;

    /// <summary>
    /// Jobs flushed and waiting to be picked up by worker threads
    /// </summary>
    private ConcurrentQueue<Job> MasterQueue { get; }

    // sets up all the various properties; called by the constructor of JobScheduler
    private void InitAlgorithm(int threadCount, int maxJobs, CancellationToken token)
    {
        _stealBound = 2 * (threadCount - 1);
        _workers = new WorkerData[threadCount];

        for (int i = 0; i < _workers.Length; i++)
        {
            _workers[i] = new WorkerData(i, maxJobs);
        }
        _token = token;
    }


    // Algorithm 2 [1]
    private void WorkerLoop(object data)
    {
        var worker = (int)data;
        Job? task = null;
        var workerData = _workers[worker];
        while (true)
        {
            // execute the task
            ExploitTask(ref task, workerData);
            // steal and/or wait for the next task
            if (!WaitForTask(ref task, workerData))
            {
                break;
            }
        }

        // allows us to track thread disposal; differs from Lin et al
        if (Interlocked.Decrement(ref _threadsAlive) == 0)
        {
            // if we're the last thread active, we don't need this event
            // to unblock potentially active threads anymore.
            _notifier.Dispose();
        }
    }

    // Actually do the execution of a task
    // In Lin et al. the functionality of this method is merely implied.
    // The idea is that a task will execute, and then push any newly resolved dependencies to its cache and queue.
    // So this is the least clearly-indicated method from Lin, and has the most pollution from our own job-tracking API. (JobInfoPool etc).
    private void Execute(in Job task, WorkerData workerData)
    {
        // it might be null if this is a job generated with CombineDependencies
        var readyDependencies = workerData.ReadyDependencyCache;
        readyDependencies.Clear();
        task.Execute(readyDependencies);

        if (readyDependencies.Count > 0)
        {
            // Cache the first unlocked dependency for quick access
            workerData.Cache = readyDependencies[0];

            // Queue up any others
            for (int i = 1; i < readyDependencies.Count; i++)
            {
                Push(readyDependencies[i], workerData);
            }
        }
        else
        {
            // If we didn't find anything, we clear the cache
            workerData.Cache = null;
        }
    }

    // Pops from our own deque
    private void Pop(out Job? task, WorkerData workerData)
    {
        if (workerData.Deque.TryPopBottom(out var popped))
        {
            task = popped;
        }
        else
        {
            task = null;
        }
    }

    // Pushes to our own deque
    private void Push(in Job task, WorkerData workerData)
    {
        workerData.Deque.PushBottom(task);
    }

    // Steals from a victim's deque
    private void StealFrom(out Job? task, WorkerData workerData)
    {
        if (workerData.Deque.TrySteal(out var stolen))
        {
            task = stolen;
        }
        else
        {
            task = null;
        }
    }

    /// <summary>
    /// Resolves this thread's entire deque and cache; returns when empty;
    /// </summary>
    /// <remarks>
    /// Based on Algorithm 3 of Lin et al. [1]
    /// </remarks>
    /// <param name="task"></param>
    /// <param name="workerData"></param>
    private void ExploitTask(ref Job? task, WorkerData workerData)
    {
        // if we incremented _numActives from 0 to 1, and there aren't any thieves currently active.
        // it means we need to notify additional threads to pick up more work, because they aren't pulling their weight.
        if (Interlocked.Increment(ref _numActives) == 1 && _numThieves == 0)
        {
            _notifier.NotifyOne();
        }

        do
        {
            if (task is not null)
            {
                Execute(task, workerData);
            }
            if (workerData.Cache is not null)
            {
                // We use a cached task before our deque, if available, for quick access
                task = workerData.Cache;
            }
            else
            {
                // Otherwise, we just pop from our deque until it's empty
                Pop(out task, workerData);
            }
        }
        while (task is not null);

        Interlocked.Decrement(ref _numActives);
    }

    /// <summary>
    /// Steals or waits for a task.
    /// </summary>
    /// <remarks>
    /// Based on Algorithm 5 of Lin et al. [1]
    /// </remarks>
    /// <param name="task"></param>
    /// <param name="workerData"></param>
    /// <returns></returns>
    private bool WaitForTask(ref Job? task, WorkerData workerData)
    {
    WaitForTask:
        // It's stealing time!
        Interlocked.Increment(ref _numThieves);
        // Do the actual steal
        ExploreTask(ref task, workerData);

        // If we succeeded in stealing, return true.
        if (task is not null)
        {
            // If we were the last thief active, we'd better wake another one up.
            if (Interlocked.Decrement(ref _numThieves) == 0)
            {
                _notifier.NotifyOne();
            }
            return true;
        }

        // We failed stealing even after multiple tries, so we have to wait.

        // First, though, we check the master queue in case anything's been added. This is the only point where
        // we access the master queue, so it's important to capture new jobs.
        if (MasterQueue.TryDequeue(out var stolenTask))
        {
            // oh wait, we actually can take from the master queue.
            task = stolenTask;

            // we succeeded in taking!
            // if we were the last thief active, we'd better wake another one up.
            if (Interlocked.Decrement(ref _numThieves) == 0)
            {
                _notifier.NotifyOne();
            }
            return true;
        }

        // NOTE: The original algorithm has a check on the queue count prior to TryDequeue. Then, if the TryDequeue fails,
        // it tries the whole entire stealing algorithm again!
        // I.e., when a worker looks at the master queue count, and tries to steal but fails, it means some other worker stole
        // instead and the queue is now empty.
        // I'm not sure why they do that, and they don't explain.
        // But I think it's not relevant anymore since we're not using Eventcount and we're using a ConcurrentQueue.

        // If we're finished, don't start waiting
        if (_token.IsCancellationRequested)
        {
            // Tell everyone else we're finished
            _notifier.NotifyAll();
            Interlocked.Decrement(ref _numThieves);
            return false;
        }

        // If we were the last thief, but there are actives, we try again (pretend like we're a newly notified thief).
        if (Interlocked.Decrement(ref _numThieves) == 0 && _numActives > 0)
        {
            goto WaitForTask;
        }

        // We wait here, now that we're positive no work remains.
        _notifier.Wait();
        return true;
    }

    /// <summary>
    /// Runs the stealing algorithm, the key insight of Lin et al.
    /// It steals some number of times, then begins yielding between steals, and then after a number of failed yields,
    /// returns. If at any point it finds a task, it sets <paramref name="task"/> to the found task and returns.
    /// </summary>
    /// <remarks>
    /// Based on Algorithm 4 of Lin et al. [1]
    /// </remarks>
    /// <param name="task"></param>
    /// <param name="workerData"></param>
    private void ExploreTask(ref Job? task, WorkerData workerData)
    {
        int numFailedSteals = 0;
        int numYields = 0;

        while (!_token.IsCancellationRequested)
        {
            var victim = GetRandomThread();
            if (victim.Id == workerData.Id)
            {
                // If we randomly choose ourselves we treat that as the master queue, and steal from there
                if (MasterQueue.TryDequeue(out var stolen))
                {
                    task = stolen;
                }
            }
            else
            {
                // Otherwise we steal from the victim!
                StealFrom(out task, victim);
            }

            // Steal success!
            if (task is not null)
            {
                break;
            }

            // Steal failed.
            numFailedSteals++;
            if (numFailedSteals >= _stealBound)
            {
                // If we've failed too many steals, we need to start yielding between steals.
                Thread.Yield();
                numYields++;
                // If we've yielded too much, we give up completely and let WaitForTask decide whether to put us back to sleep (maybe)
                if (numYields == _yieldBound)
                {
                    break;
                }
            }
        }
    }

    private WorkerData GetRandomThread()
    {
        var worker = _random.Next(0, _workers.Length);
        return _workers[worker];
    }
}
