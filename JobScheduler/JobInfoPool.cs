using Microsoft.Extensions.ObjectPool;
using System.Diagnostics;

namespace JobScheduler;

/// <summary>
/// Tracks a group of stored <see cref="JobInfo"/>s, associating them with a unique ID, and removes them from memory for pooling at the earliest opportunity.
/// NOT thread safe, must use locks during interaction!
/// </summary>
internal class JobInfoPool
{
    int nextID = 0;

    // a list indexed by job ID; we can reuse job IDs once they're complete
    private List<JobInfo?> Infos { get; } = new(32);

    // the jobIDs to reuse, as well as the last version used
    private Queue<(int ID, int Version)> ReusedIDs { get; } = new(32);


    // A pool of handles to use for everything.
    private DefaultObjectPool<ManualResetEvent> ManualResetEventPool { get; } = new(new ManualResetEventPolicy(), 32);

    public struct JobInfo
    {
        public JobInfo(int jobID, int version, ManualResetEvent waitHandle)
        {
            JobID = jobID;
            WaitHandle = waitHandle;
            JobIDVersion = version;
        }

        public int JobID { get; }

        public int JobIDVersion { get; }

        /// <summary>
        /// The Handle of the job. 
        /// </summary>
        public ManualResetEvent WaitHandle { get; }

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
    /// Create a new <see cref="JobInfo"/> in the pool, with an optional dependency
    /// </summary>
    /// <returns>The <see cref="JobID"/> of the created job</returns>
    public JobID Schedule()
    {
        int version = 0;
        int id = -1;
        if (ReusedIDs.TryDequeue(out var ids))
        {
            id = ids.ID;
            version = ids.Version + 1;
        }

        // no IDs left to reuse, so we need to make a new one
        if (id == -1)
        {
            id = nextID;
            nextID++;
        }

        JobInfo info = new(id, version, ManualResetEventPool.Get());

        info.WaitHandle.Reset(); // must reset when acquiring

        while (Infos.Count <= id)
        {
            Infos.Add(null);
        }
        Infos[id] = info;
        return new(id, version);
    }

    /// <summary>
    /// Get the <see cref="JobInfo"/> associated with an ID 
    /// </summary>
    /// <param name="jobID"></param>
    /// <returns></returns>
    public JobInfo GetInfo(JobID jobID)
    {
        ValidateJobNotComplete(jobID);
        return Infos[jobID.ID]!.Value; // we ensured that we weren't complete, so this must exist
    }

    /// <summary>
    /// Mark a job as Complete, removing it from circulation entirely. The WaitHandle is not disposed yet
    /// </summary>
    /// <param name="jobID"></param>
    public ManualResetEvent? MarkComplete(JobID jobID)
    {
        ValidateJobNotComplete(jobID);
        var job = Infos[jobID.ID]!.Value;
        job.IsComplete = true;
        Infos[jobID.ID] = job;

        if (job.WaitHandleSubscriptionCount != 0)
        {
            // we have subscribers, so we give up the handle to allow pinging, and keep it until we've resolved subscribers
            Infos[jobID.ID] = job;
            return job.WaitHandle;
        }
        else
        {
            // we do not have subscribers, so the handle was never used
            ReturnJobID(jobID.ID, job);
            return null;
        }
    }

    /// <summary>
    /// Return a handle to the pool. Only valid when <see cref="SubscribeToHandle"/> was called and the produced handle was waited for.
    /// </summary>
    /// <param name="jobID"></param>
    public void ReturnHandle(JobID jobID)
    {
        // decrement our subscribed handles for this jobID
        // ensures that we only dispose once all callers have gotten the message
        var job = Infos[jobID.ID];

        // ensure we haven't already returned it prematurely
        Debug.Assert(job is not null);
        // ensure we're actually in a complete status now
        Debug.Assert(job.Value.IsComplete);
        // ensure we somehow haven't reused it
        Debug.Assert(job.Value.JobIDVersion == jobID.Version);

        var jobVal = job.Value;

        jobVal.WaitHandleSubscriptionCount--;
        Infos[jobID.ID] = jobVal;
        if (jobVal.WaitHandleSubscriptionCount == 0)
        {
            ReturnJobID(jobID.ID, jobVal);
        }
    }

    private void ReturnJobID(int jobID, in JobInfo info)
    {
        Debug.Assert(Infos[jobID] is not null);
        Debug.Assert(Infos[jobID]!.Value.JobIDVersion == info.JobIDVersion);
        Debug.Assert(Infos[jobID]!.Value.IsComplete);
        Infos[jobID] = null;
        ReusedIDs.Enqueue((jobID, info.JobIDVersion));
        ManualResetEventPool.Return(info.WaitHandle);
    }

    public ManualResetEvent SubscribeToHandle(JobID jobID)
    {
        ValidateJobNotComplete(jobID); // ensure we're still on the right version
        var job = Infos[jobID.ID]!.Value;

        // increment our subscribed handles for this jobID
        // ensures that we only dispose once all callers have gotten the message
        job.WaitHandleSubscriptionCount++;
        Infos[jobID.ID] = job;

        return job.WaitHandle;
    }

    /// <summary>
    /// Returns whether the job is complete (i.e. removed from the pool)
    /// </summary>
    /// <param name="jobID"></param>
    /// <returns></returns>
    public bool IsComplete(JobID jobID)
    {
        ValidateJobID(jobID);
        var job = Infos[jobID.ID];
        if (job is null) return true; // if it's missing, we've completed and removed it, and its ID hasn't been reused yet
        if (job.Value.JobIDVersion != jobID.Version) return true; // if we're a different version, the jobID is long since completed and reused
        if (job.Value.IsComplete) return true; // we're the right version, and therefore waiting on some handles to Complete(), but we are definitely complete
        return false;
    }

    [Conditional("DEBUG")]
    private void ValidateJobID(JobID jobID)
    {
        if (jobID.ID < 0) throw new ArgumentOutOfRangeException("Job ID not valid!");
        if (jobID.ID >= nextID) throw new ArgumentOutOfRangeException("Job ID doesn't exist yet!");
    }

    [Conditional("DEBUG")]
    private void ValidateJobNotComplete(JobID jobID)
    {
        if (IsComplete(jobID)) throw new InvalidOperationException("Job is already complete!");
    }
}
