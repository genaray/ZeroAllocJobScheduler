using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Schedulers;

/// <summary>
/// The <see cref="JobHandle"/> struct
/// is used to control and await a scheduled <see cref="IJob"/>.
/// <remarks>Size is exactly 64 bytes to fit perfectly into one default sized cacheline to reduce false sharing and be more efficient.</remarks>
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 64)]
public class JobHandle
{
    internal readonly IJob _job;

    internal readonly JobHandle _parent;
    internal int _unfinishedJobs;

    internal readonly List<JobHandle> _dependencies;

    private long _padding1;
    private long _padding2;
    private short _padding3;
    private short _padding4;

    /// <summary>
    /// Creates a new <see cref="JobHandle"/>.
    /// </summary>
    /// <param name="job">The job.</param>
    public JobHandle(IJob job)
    {
        _job = job;
        _parent = null;
        _unfinishedJobs = 1;
        _dependencies = [];
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
        _dependencies = [];
    }
}
