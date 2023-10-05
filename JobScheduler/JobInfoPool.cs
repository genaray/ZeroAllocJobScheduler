using Microsoft.Extensions.ObjectPool;
using System.Diagnostics;

namespace JobScheduler;

/// <summary>
/// Tracks a group of stored <see cref="JobInfo"/>s, associating them with a unique ID, and removes them from memory for pooling at the earliest opportunity.
/// NOT thread safe, must use locks during interaction!
/// </summary>
internal class JobInfoPool
{
    // This class operates on the assumption that, if a JobID of lesser ID is no longer present, 

    int nextID = 0;

    // consider using this for allocation-friendliness: https://github.com/Wsm2110/Faster.Map/blob/main/src/FastMap.cs
    private Dictionary<int, JobInfo> Infos { get; } = new();

    // Along with the other data, we need to keep track of if we can dispose the handle of a JobID
    private Dictionary<int, int> WaitHandleSubscriptionCounts { get; } = new();

    // A pool of handles to use for everything.
    private DefaultObjectPool<ManualResetEvent> ManualResetEventPool { get; } = new(new ManualResetEventPolicy());

    public struct JobInfo
    {
        public JobInfo(int jobID, ManualResetEvent waitHandle)
        {
            JobID = jobID;
            WaitHandle = waitHandle;
        }

        public int JobID { get; }

        /// <summary>
        /// The Handle of the job. Null if the job hasn't been flushed to threads, yet.
        /// </summary>
        public ManualResetEvent WaitHandle { get; }
        public bool Flushed { get; set; }
    }

    /// <summary>
    /// Create a new <see cref="JobInfo"/> in the pool, with an optional dependency
    /// </summary>
    /// <returns>The Job ID of the created job</returns>
    public int Schedule()
    {
        var id = nextID;
        JobInfo info = new(id, ManualResetEventPool.Get());

        info.WaitHandle.Reset(); // must reset when acquiring
        Infos[id] = info;
        nextID++;

        // we want to allocate (sometimes) during Schedule but nowhere else, and we know we'll need this later if the user calls complete, so:
        WaitHandleSubscriptionCounts[id] = 0;
        return id;
    }

    /// <summary>
    /// Get the <see cref="JobInfo"/> associated with an ID 
    /// </summary>
    /// <param name="jobID"></param>
    /// <returns></returns>
    public JobInfo GetInfo(int jobID)
    {
        ValidateJobNotComplete(jobID);
        return Infos[jobID];
    }
    
    /// <summary>
    /// Mark a job as flushed from the scheduling queue and dispatched to threads
    /// </summary>
    /// <param name="jobID"></param>
    /// <exception cref="InvalidOperationException"></exception>
    public void MarkFlushed(int jobID)
    {
        ValidateJobNotComplete(jobID);
        var info = Infos[jobID];
        if (info.Flushed) throw new InvalidOperationException("Job is already flushed!");
        info.Flushed = true;
        Infos[jobID] = info;
    }

    /// <summary>
    /// Mark a job as Complete, removing it from circulation entirely. The WaitHandle is not disposed yet
    /// </summary>
    /// <param name="jobID"></param>
    public ManualResetEvent? MarkComplete(int jobID)
    {
        ValidateJobNotComplete(jobID);
        var job = Infos[jobID];
        Infos.Remove(jobID);

        if (WaitHandleSubscriptionCounts[jobID] != 0)
        {
            // we have subscribers, so we give up the handle to allow pinging
            return job.WaitHandle;
        }
        else
        {
            // we do not have subscribers, so the handle was never used
            WaitHandleSubscriptionCounts.Remove(jobID);
            ManualResetEventPool.Return(job.WaitHandle);
            return null;
        }
    }

    public void ReturnHandle(int jobID, ManualResetEvent handle)
    {
        // decrement our subscribed handles for this jobID
        // ensures that we only dispose once all callers have gotten the message
        if (WaitHandleSubscriptionCounts.ContainsKey(jobID)) WaitHandleSubscriptionCounts[jobID]--;
        if (WaitHandleSubscriptionCounts[jobID] == 0)
        {
            WaitHandleSubscriptionCounts.Remove(jobID);
            ManualResetEventPool.Return(handle);
        }
    }

    public ManualResetEvent SubscribeToHandle(int jobID)
    {
        ValidateJobNotComplete(jobID);
        var job = Infos[jobID];

        // increment our subscribed handles for this jobID
        // ensures that we only dispose once all callers have gotten the message
        if (!WaitHandleSubscriptionCounts.ContainsKey(jobID)) WaitHandleSubscriptionCounts[jobID] = 0;
        WaitHandleSubscriptionCounts[jobID]++;

        return job.WaitHandle;
    }

    /// <summary>
    /// Returns whether the job is complete (i.e. removed from the pool)
    /// </summary>
    /// <param name="jobID"></param>
    /// <returns></returns>
    public bool IsComplete(int jobID)
    {
        ValidateJobID(jobID);
        return !Infos.ContainsKey(jobID);
    }

    /// <summary>
    /// Returns whether a given job is Flushed.
    /// </summary>
    /// <param name="jobID"></param>
    /// <returns></returns>
    public bool IsFlushed(int jobID)
    {
        ValidateJobID(jobID);
        if (IsComplete(jobID)) return true;
        return Infos[jobID].Flushed;
    }

    [Conditional("DEBUG")]
    private void ValidateJobID(int jobID)
    {
        if (jobID < 0) throw new ArgumentOutOfRangeException("Job ID not valid!");
        if (jobID >= nextID) throw new ArgumentOutOfRangeException("Job ID doesn't exist yet!");
    }

    [Conditional("DEBUG")]
    private void ValidateJobNotComplete(int jobID)
    {
        if (IsComplete(jobID)) throw new InvalidOperationException("Job is already complete!");
    }
}
