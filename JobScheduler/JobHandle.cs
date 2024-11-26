using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Schedulers;

/// <summary>
/// The <see cref="JobHandle"/> struct
/// is used to control and await a scheduled <see cref="IJob"/>.
/// <remarks>Size is exactly 64 bytes to fit perfectly into one default sized cacheline to reduce false sharing and be more efficient.</remarks>
/// </summary>
public class JobHandle
{
    internal IJob _job;
    internal JobHandle? _parent;
    private List<JobHandle>? _dependencies;
    internal int _unfinishedJobs;
    internal int index = -1;//-1 for non pooled and 0 or higher for pooled
    public int generation = 0;
    /// <summary>
    /// Creates a new <see cref="JobHandle"/>.
    /// </summary>
    /// <param name="job">The job.</param>
    public JobHandle(IJob job)
    {
        _job = job;
        _parent = null;
        _unfinishedJobs = 1;
        _dependencies = null;
    }

    public JobHandle(int index)
    {
        this.index = index;
    }

    public void ReinitializeWithJob(IJob job)
    {
        _job = job;
        _parent = null;
        _unfinishedJobs = 1;
        _dependencies = null;
    }

    public bool HasDependencies()
    {
        return _dependencies is { Count: > 0 };
    }

    public List<JobHandle> GetDependencies()
    {
        return _dependencies ??= [];
    }

    /// <summary>
    /// Creates a new <see cref="JobHandle"/>.
    /// </summary>
    /// <param name="job">The job.</param>
    /// <param name="parent">Its parent.</param>
    public JobHandle(IJob job, JobHandle parent)
    {
        _job = job;
        _parent = parent;
        _unfinishedJobs = 1;
        _dependencies = null;
    }
}
