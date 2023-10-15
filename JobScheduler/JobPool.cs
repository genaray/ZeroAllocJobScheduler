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
    public Job(JobId jobId, ManualResetEvent? waitHandle)
    {
        JobId = jobId;
        WaitHandle = waitHandle;
    }

    internal JobId JobId { get; }

    /// <summary>
    /// The Handle of the job. 
    /// </summary>
    public ManualResetEvent? WaitHandle { get; }

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

        for (int i = 0; i < capacity; i++) ManualResetEventPool.Return(new(false));
    }

    /// <summary>
    /// Create a new <see cref="Job"/> in the pool, with an optional dependency
    /// </summary>
    /// <returns>The <see cref="JobId"/> of the created job</returns>
    public JobId Schedule()
    {
        if (!_recycledIds.TryDequeue(out JobId id))
        {
            id = new JobId(_nextId, 1);
            _nextId++;
        }

        var job = new Job(id, ManualResetEventPool.Get());
        Debug.Assert(job.WaitHandle is not null);
        job.WaitHandle.Reset(); // must reset when acquiring

        // Adjust array size
        if (_jobs.Length <= id.Id)
        {
            Array.Resize(ref _jobs, _jobs.Length * 2);    
        }
        
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

        _jobs[jobId.Id] = new(new(-1, -1), null);
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
    ///     Mark a <see cref="Job"/> as Complete by its <see cref="JobId"/>.
    ///     Removing it from circulation entirely. The <see cref="ManualResetEvent"/> is not disposed yet.
    /// </summary>
    /// <param name="jobId">The <see cref="JobId"/>.</param>
    public ManualResetEvent? MarkComplete(JobId jobId)
    {
        ValidateJobNotComplete(jobId);
        var job = _jobs[jobId.Id];
        job.IsComplete = true;
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
