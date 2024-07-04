using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Schedulers;

/// <summary>
/// The <see cref="JobHandle"/> struct
/// is used to control and await a scheduled <see cref="IJob"/>.
/// <remarks>Size is exactly 32 bytes to fit perfectly into one default sized cacheline to reduce false sharing and be more efficient.</remarks>
/// </summary>
///
[StructLayout(LayoutKind.Sequential, Size = 64)]
public class JobHandle
{
    internal readonly IJob _job;

    internal readonly JobHandle _parent;
    internal int _unfinishedJobs;

    internal readonly List<JobHandle> _dependencies;

    private long _padding1;
    private long _padding2;
    private long _padding3;
    private int _padding4;

    public JobHandle(IJob job)
    {
        _job = job;
        _parent = null;
        _unfinishedJobs = 1;
        _dependencies = new List<JobHandle>();
    }

    public JobHandle(IJob job, JobHandle parent)
    {
        _job = job;
        _parent = parent;
        _unfinishedJobs = 1;
        _dependencies = new List<JobHandle>();
    }
}
