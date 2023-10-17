using DequeNet;

namespace JobScheduler;

// This section of JobScheduler deals with the implementation of the algorithm found here: https://tsung-wei-huang.github.io/papers/icpads20.pdf [1]
// [1] Lin, C.-X., Huang, T.-W., & Wong, M. D. (2020). An efficient work-stealing scheduler for task dependency graph. 2020 IEEE 26th
//      International Conference on Parallel and Distributed Systems (ICPADS). https://doi.org/10.1109/icpads51040.2020.00018 
public partial class JobScheduler
{
    // The Lin et al. version uses an Eventcount, which I don't fully understand and definitely can't implement.
    // Here's the best compilation of documentation I've found on them: https://gist.github.com/mratsim/04a29bdd98d6295acda4d0677c4d0041
    // I haven't seen any .NET implementations.
    // For now, this is fine: the requirement the paper presents is that the notifier must be able to wake a single thread, or multiple threads, and wait.
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
                if (IsDisposed) return;
                _multipleNotifier.Set();
            }
        }

        private readonly object _disposeLock = new();

        public void Dispose()
        {
            lock (_disposeLock)
            {
                if (IsDisposed) return;
                IsDisposed = true;
                _singleNotifier.Dispose();
                _multipleNotifier.Dispose();
            }
        }
    }

    private class WorkerData
    {
        public WorkerData(int id, int maxJobs)
        {
            Id = id;
            ReadyDependencyCache = new(maxJobs - 1);
            Deque = new(maxJobs);
        }
        public int Id { get; }
        public JobMeta? Cache { get; set; } = null;

        // We're using DequeNET https://github.com/dcastro/DequeNET. We can either include the Nuget package
        // or grab the code (it's MIT licensed). It's based on the Michael queue. [2]
        // [2] Michael, Maged, 2003, CAS-Based Lock-Free Algorithm for Shared Deques, Euro-Par 2003 Parallel Processing, v. 2790, p. 651-660,
        // http://www.research.ibm.com/people/m/michael/europar-2003.pdf (Decembre 22, 2013).
        // ^ Link is dead PDF is findable.
        // 
        // The massive issue is that ConcurrentDeque here is not alloc-free! It makes no effort to reuse nodes.
        // We use instead the array-based Deque in the same package, but we have to lock :(
        //
        // However, Lin et al. use the Chase-Lev deque: https://www.dre.vanderbilt.edu/~schmidt/PDF/work-stealing-dequeue.pdf [3]
        // The HUGE advantage to that is that the Chase-Lev deque is circular array-based AND ALSO lock-free. So it's ideal for our case.
        // [3] David Chase and Yossi Lev. Dynamic circular work-stealing deque. In SPAA, pages 21–28. ACM, 2005.
        // A Chase-Lev deque is here, in C++ https://github.com/ConorWilliams/ConcurrentDeque (Mozilla Public License 2.0; weak copyleft)
        // Here's one in Rust http://huonw.github.io/parry/deque/index.html (Apache 2.0)
        // Nobody's made a C# implementation yet. But we could; it looks doable. The original paper is in Java which makes it easier.
        public Deque<JobMeta> Deque { get; }
        public object DequeLock = new();

        // store this to cache the output from JobPool per thread
        public List<(JobId, IJob?)> ReadyDependencyCache { get; }
    }

    private int _stealBound = 0;
    private readonly int _yieldBound = 100;

    private int _numActives = 0;
    private int _numThieves = 0;

    private readonly Notifier _notifier = new();
    private readonly Random _random = new();

    private WorkerData[] _workers = null!;
    private CancellationToken _token;

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


    // algorithm 2
    private void WorkerLoop(object data)
    {
        var worker = (int)data;
        JobMeta? task = null;
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

        if (Interlocked.Decrement(ref _threadsAlive) == 0)
        {
            // if we're the last thread active, we don't need this event
            // to unblock potentially active threads anymore.
            _notifier.Dispose();
        }
    }

    // actually do the execution of a task
    private void Execute(in JobMeta task, WorkerData workerData)
    {
        // it might be null if this is a job generated with CombineDependencies
        task.Job?.Execute();

        // the purpose of this lock is to ensure that the Complete method always subscribes and listens to an existant signal.
        ManualResetEvent? handle;
        var readyDependencies = workerData.ReadyDependencyCache;
        readyDependencies.Clear();
        lock (JobPool)
        {
            // remove the job from circulation
            handle = JobPool.MarkComplete(task.JobID, readyDependencies);
        }

        if (readyDependencies.Count > 0)
        {
            // queue up in our personal queue for work-stealing
            // cache the first one
            workerData.Cache = new(readyDependencies[0].Item1, readyDependencies[0].Item2);

            // queue up any additionals
            lock (workerData.DequeLock)
            {
                for (int i = 1; i < readyDependencies.Count; i++)
                {
                    var tup = readyDependencies[i];
                    workerData.Deque.PushLeft(new JobMeta(tup.Item1, tup.Item2));
                }
            }
        }
        else
        {
            workerData.Cache = null;
        }

        // If JobScheduler.Complete was called on this job by a different thread, it told the job pool with Subscribe that we should ping,
        // and that Complete would handle recycling. We notify the event here.
        handle?.Set();
    }

    // pops from our own queue
    private void Pop(out JobMeta? task, WorkerData workerData)
    {
        task = null;
        lock (workerData.DequeLock)
        {
            if (workerData.Deque.IsEmpty) return;
            task = workerData.Deque.PopLeft();
        }
    }

    // steals from a victim's queue
    private void StealFrom(out JobMeta? task, WorkerData workerData)
    {
        task = null;
        lock (workerData.DequeLock)
        {
            if (workerData.Deque.IsEmpty) return;
            task = workerData.Deque.PopRight();
        }
    }

    // algorithm 3
    private void ExploitTask(ref JobMeta? task, WorkerData workerData)
    {
        // if we incremented _numActives from 0 to 1, and there aren't any thieves currently active.
        // it means we need to notify additional threads to pick up more work, because they aren't pulling their weight.
        if (Interlocked.Increment(ref _numActives) == 1 && _numThieves == 0)
        {
            _notifier.NotifyOne();
        }

        do
        {
            if (task is not null) Execute(task.Value, workerData);
            if (workerData.Cache is not null)
            {
                // we cache our next task to work on, if available
                task = workerData.Cache;
            }
            else
            {
                // otherwise, we just pop from our queue
                Pop(out task, workerData);
            }
        }
        while (task is not null);

        Interlocked.Decrement(ref _numActives);
    }

    // algorithm 5
    private bool WaitForTask(ref JobMeta? task, WorkerData workerData)
    {
        WaitForTask:
        // it's stealing time!
        Interlocked.Increment(ref _numThieves);
        // do the actual steal
        ExploreTask(ref task, workerData);

        // if we succeeded in stealing, return true.
        if (task is not null)
        {
            // if we were the last thief active, we'd better wake another one up.
            if (Interlocked.Decrement(ref _numThieves) == 0)
            {
                _notifier.NotifyOne();
            }
            return true;
        }

        // we failed stealing even after multiple tries, so we have to wait.

        // First, though, we check the master queue in case anything's been added
        // (is this necessary without Eventcount? I'm not sure.)
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
            // tell everyone else we're finished
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

    // algorithm 4
    private void ExploreTask(ref JobMeta? task, WorkerData workerData)
    {
        int numFailedSteals = 0;
        int numYields = 0;

        while (!_token.IsCancellationRequested)
        {
            var victim = GetRandomThread();
            if (victim.Id == workerData.Id)
            {
                // if we randomly choose ourselves we treat that as the master queue, and steal from there
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
            
            // steal success!
            if (task is not null) break;

            // steal failed.
            numFailedSteals++;
            if (numFailedSteals >= _stealBound)
            {
                // if we've failed too many steals, we need to start yielding between steals.
                Thread.Yield();
                numYields++;
                // if we've yielded too much, we give up completely and let WaitForTask decide whether to put us back to sleep (maybe)
                if (numYields == _yieldBound) break;
            }
        }
    }

    private WorkerData GetRandomThread()
    {
        var worker = _random.Next(0, _workers.Length);
        return _workers[worker];
    }
}
