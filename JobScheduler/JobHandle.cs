using System.Collections.Concurrent;
using Schedulers.Utils;

namespace Schedulers;

public class EmptyJob : IJob
{
    public void Execute()
    {
    }
}

public class JobHandleSoaPool
{
    public JobHandleSoaPool()
    {
        const ushort MaxCount = ushort.MaxValue;
        _freeIds = new(MaxCount);
        Parent = new ushort[MaxCount];
        Dependencies = new List<JobHandle>?[MaxCount];
        UnfinishedJobs = new int[MaxCount];
        Jobs = new IJob[MaxCount];
    }

    public ushort[] Parent;
    public List<JobHandle>?[] Dependencies;
    public int[] UnfinishedJobs;
    public IJob[] Jobs;
    private readonly JobHandlePool _freeIds;

    public JobHandle GetNewHandle(IJob iJob)
    {
        if (iJob == null) throw new("Job cannot be null");
        _freeIds.GetHandle(out var index);
        if (index == null) throw new InvalidOperationException("No more handles available");
        return new()
        {
            Index = index.Value,
            Job = iJob,
            Parent = ushort.MaxValue,
            UnfinishedJobs = 1,
            Dependencies = null,
        };
    }

    public void ReleaseHandle(JobHandle handle)
    {
        _freeIds.ReturnHandle(handle);
    }

    public void DecrementUnfinished(ushort jobMainDependency)
    {
        var res = Interlocked.Decrement(ref UnfinishedJobs[jobMainDependency]);
        if (res < 0)
        {
            throw new InvalidOperationException("Unfinished jobs cannot be negative");
        }
    }
}

/// <summary>
/// The <see cref="JobHandle"/> struct
/// is used to control and await a scheduled <see cref="IJob"/>.
/// <remarks>Size is exactly 64 bytes to fit perfectly into one default sized cacheline to reduce false sharing and be more efficient.</remarks>
/// </summary>
public struct JobHandle
{
    public static JobHandleSoaPool Pool = new();
    public ushort Index;
    public ref IJob Job => ref Pool.Jobs[Index];

    public ref ushort Parent => ref Pool.Parent[Index];

    //In case we depend on multiple jobs
    public ref List<JobHandle>? Dependencies => ref Pool.Dependencies[Index];
    public ref int UnfinishedJobs => ref Pool.UnfinishedJobs[Index];

    public void SetDependsOn(JobHandle toDependOn)
    {
        Interlocked.Increment(ref toDependOn.UnfinishedJobs);
        Parent = toDependOn.Index;
    }

    public bool HasDependencies()
    {
        return Dependencies is { Count: > 0 };
    }

    public List<JobHandle> GetDependencies()
    {
        return Dependencies ??= [];
    }

    public JobHandle(ushort id)
    {
        Index = id;
    }
}
