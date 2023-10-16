using Microsoft.Extensions.ObjectPool;
using System.Diagnostics;

namespace JobScheduler;

/// <summary>
///     The <see cref="Job"/> struct
///     represents the Job itself with all its most important data. 
/// </summary>
internal record struct Job
{

    /// <summary>
    ///     Creates a new <see cref="Job"/> instance.
    /// </summary>
    /// <param name="jobId">The <see cref="JobId"/>.</param>
    /// <param name="waitHandle">Its <see cref="ManualResetEvent"/>.</param>
    /// <param name="dependents">The initial list of dependents this Job has</param>
    /// <param name="jobWork">The job work associated with this Job</param>
    public Job(JobId jobId, ManualResetEvent? waitHandle, List<JobId> dependents, IJob? jobWork)
    {
        JobId = jobId;
        WaitHandle = waitHandle;
        Dependents = dependents;
        JobWork = jobWork;
    }

    internal JobId JobId { get; }

    /// <summary>
    /// The actual code of the job.
    /// </summary>
    public IJob? JobWork { get; }

    /// <summary>
    /// The Handle of the job. 
    /// </summary>
    public ManualResetEvent? WaitHandle { get; }

    /// <summary>
    /// The list of dependents this job has (NOT dependencies!)
    /// When this job completes it will decrease the <see cref="DependencyCount"/> of any dependants.
    /// </summary>
    public List<JobId> Dependents { get; }

    /// <summary>
    /// The number of Dependencies (NOT dependants!) that must complete before this job can be added to the queue
    /// </summary>
    public int DependencyCount { get; set; } = 0;

    /// <summary>
    /// When this hits 0, we can dispose the WaitHandle, and the JobID
    /// </summary>
    public int WaitHandleSubscriptionCount { get; set; } = 0;

    /// <summary>
    /// We're complete, but the WaitHandle might not be disposed. ISSUE HERE: We might go over x concurrent jobs while we wait for anyone
    /// in the process of calling Complete() to finish up
    /// </summary>
    public bool IsComplete { get; set; }
}

/// <summary>
///     The <see cref="JobPool"/> class
///     tracks a group of stored <see cref="Job"/>s, associating them with a unique ID, and removes them from memory for pooling at the earliest opportunity.
///     NOT thread safe, must use locks during interaction!
/// </summary>
internal class JobPool
{
    /// <summary>
    ///     The size of the <see cref="JobPool"/>.
    /// </summary>
    private int _nextId;

    // a list indexed by job ID; we can reuse job IDs once they're complete.
    private Job[] _jobs;

    // the jobIDs to reuse, as well as the last version used
    private readonly Queue<JobId> _recycledIds;

    // A pool of handles to use for everything.
    private DefaultObjectPool<ManualResetEvent> ManualResetEventPool { get; }

    /// <summary>
    /// The amount of concurrent jobs currently in the <see cref="JobPool"/>.
    /// </summary>
    public int JobCount { get; private set; }

    public JobPool(int capacity)
    {
        _jobs = new Job[capacity];
        _recycledIds = new(capacity);
        ManualResetEventPool = new(new ManualResetEventPolicy(), capacity);

        for (int i = 0; i < capacity; i++)
        {
            ManualResetEventPool.Return(new(false));
            _jobs[i] = new Job(new(-1, -1), null, new(capacity - 1), null);
        }
    }

    /// <summary>
    /// Create a new <see cref="Job"/> in the pool, with an optional dependency
    /// </summary>
    /// <returns>The <see cref="JobId"/> of the created job</returns>
    public JobId Schedule(List<JobId> dependencies, IJob? work, out bool ready)
    {
        if (!_recycledIds.TryDequeue(out JobId id))
        {
            id = new JobId(_nextId, 1);
            _nextId++;
        }

        // Adjust array size
        if (_jobs.Length <= id.Id)
        {
            int oldSize = _jobs.Length;
            Array.Resize(ref _jobs, _jobs.Length * 2);
            // make lists
            for (int i = oldSize; i < _jobs.Length; i++)
            {
                // don't worry about allocating a size of the list; if we're resizing we're spontaneously allocating anyways so we don't want to overuse memory
                _jobs[i] = new Job(new(-1, -1), null, new(), null);
            }
        }

        int dependencyCount = 0;
        if (dependencies is not null)
        {
            foreach (var dependency in dependencies)
            {
                ValidateJobId(dependency);
                if (IsComplete(dependency)) continue;
                dependencyCount++;
                var d = _jobs[dependency.Id];
                d.Dependents.Add(id);
            }
        }

        ready = dependencyCount == 0; // we don't have any active dependencies

        var job = new Job(id, ManualResetEventPool.Get(), _jobs[id.Id].Dependents, work);
        Debug.Assert(job.Dependents.Count == 0);
        Debug.Assert(job.WaitHandle is not null);
        job.WaitHandle.Reset(); // must reset when acquiring
        job.DependencyCount = dependencyCount;

        _jobs[id.Id] = job;
        JobCount++;
        return id;
    }
    

    /// <summary>
    ///     Returns a <see cref="Job"/> back to the pool by its <see cref="JobId"/> and recycles it.
    /// </summary>
    /// <param name="jobId">Its <see cref="JobId"/>.</param>
    private void Return(JobId jobId)
    {
        var job = _jobs[jobId.Id];
        Debug.Assert(job.IsComplete);
        Debug.Assert(job.WaitHandle is not null);
        Debug.Assert(job.Dependents.Count == 0); // we should've already processed dependents

        _jobs[jobId.Id] = new(new(-1, -1), null, job.Dependents, null);
        JobCount--;
        jobId.Version++;
        
        _recycledIds.Enqueue(jobId);
        ManualResetEventPool.Return(job.WaitHandle);
    }
    
    
    /// <summary>
    ///     Awaits a <see cref="Job"/> by its <see cref="JobId"/> and increases its <see cref="Job.WaitHandleSubscriptionCount"/>.
    /// </summary>
    /// <param name="jobId">The <see cref="JobId"/>.</param>
    /// <returns>The <see cref="ManualResetEvent"/> from the <see cref="Job"/>.</returns>
    public ManualResetEvent AwaitJob(JobId jobId)
    {
        ValidateJobNotComplete(jobId); // ensure we're still on the right version
        var job = _jobs[jobId.Id];
        Debug.Assert(job.WaitHandle is not null);

        // increment our subscribed handles for this jobID
        // ensures that we only dispose once all callers have gotten the message
        job.WaitHandleSubscriptionCount++;
        _jobs[jobId.Id] = job;

        return job.WaitHandle;
    }
    
    /// <summary>
    ///     Sets a <see cref="Job"/>, decreases its <see cref="Job.WaitHandleSubscriptionCount"/> and moves its <see cref="Job.WaitHandle"/> back to the pool once finished.
    ///     Only valid when <see cref="AwaitJob"/> was called and the produced handle was waited for.
    /// </summary>
    /// <param name="jobId"></param>
    public void SetJob(JobId jobId)
    {
        // decrement our subscribed handles for this jobID
        // ensures that we only dispose once all callers have gotten the message
        var job = _jobs[jobId.Id];

        // ensure we haven't already returned it prematurely
        // ensure we're actually in a complete status now
        Debug.Assert(job.IsComplete);
        // ensure we somehow haven't reused it
        Debug.Assert(job.JobId.Version == jobId.Version);
        
        job.WaitHandleSubscriptionCount--;
        _jobs[jobId.Id] = job;
        if (job.WaitHandleSubscriptionCount <= 0)
        {
            Return(jobId);
        }
    }

    /// <summary>
    ///     Mark a <see cref="Job"/> as Complete by its <see cref="JobId"/>, removing it from circulation entirely.
    ///     The <see cref="ManualResetEvent"/> is not disposed yet, unless there are no subscribers.
    /// </summary>
    /// <param name="jobId">The <see cref="JobId"/>.</param>
    /// <param name="readyDependencies">Filled with any dependencies that are now ready for execution.</param>
    /// <returns>The <see cref="ManualResetEvent"/>, if there were subscribers that the caller must notify.</returns>
    public ManualResetEvent? MarkComplete(JobId jobId, List<(JobId ID, IJob? Work)> readyDependencies)
    {
        ValidateJobNotComplete(jobId);
        var job = _jobs[jobId.Id];
        job.IsComplete = true;
        foreach (var dependent in job.Dependents)
        {
            var d = _jobs[dependent.Id];
            Debug.Assert(d.DependencyCount > 0);
            d.DependencyCount--;
            if (d.DependencyCount == 0)
            {
                readyDependencies.Add((dependent, d.JobWork));
            }
            _jobs[dependent.Id] = d;
        }

        job.Dependents.Clear();
        _jobs[jobId.Id] = job;

        if (job.WaitHandleSubscriptionCount > 0)
        {
            // we have subscribers, so we give up the handle to allow pinging, and keep it until we've resolved subscribers
            return job.WaitHandle;
        }

        // we do not have subscribers, so the handle was never used
        Return(jobId);
        return null;
    }

    /// <summary>
    /// Returns whether this job is ready to run, i.e. has 0 incomplete dependencies.
    /// </summary>
    public bool IsReady(JobId jobId)
    {
        ValidateJobNotComplete(jobId);
        var job = _jobs[jobId.Id];
        return job.DependencyCount == 0;
    }
    

    /// <summary>
    /// Returns whether the job is complete (i.e. removed from the pool)
    /// </summary>
    /// <param name="jobId"></param>
    /// <returns></returns>
    public bool IsComplete(JobId jobId)
    {
        ValidateJobId(jobId);
        var job = _jobs[jobId.Id];
        return job.JobId.Id == -1 || // if it's missing, we've completed and removed it, and its ID hasn't been reused yet
               job.JobId.Version != jobId.Version || // if we're a different version, the jobID is long since completed and reused
               job.IsComplete; // we're the right version, and therefore waiting on some handles to Complete(), but we are definitely complete
    }

    [Conditional("DEBUG")]
    private void ValidateJobId(JobId jobId)
    {
        if (jobId.Id < 0) throw new ArgumentOutOfRangeException("Job ID not valid!");
        if (jobId.Id >= _nextId) throw new ArgumentOutOfRangeException("Job ID doesn't exist yet!");
    }

    [Conditional("DEBUG")]
    private void ValidateJobNotComplete(JobId jobId)
    {
        if (IsComplete(jobId)) throw new InvalidOperationException("Job is already complete!");
    }
}
