using System.Collections.Concurrent;
using Schedulers.Utils;

namespace Schedulers;

/// <summary>
/// The <see cref="JobHandleSoaPool"/> class
/// acts as a pool for meta-data of a <see cref="JobHandle"/> to reduce allocations.
/// </summary>
public class JobHandleSoaPool
{
    /// <summary>
    /// An array that stores the parent of each <see cref="JobHandle"/>.
    /// </summary>
    internal readonly ushort[] Parent;

    /// <summary>
    /// An array that stores a list of <see cref="Dependencies"/> of each <see cref="JobHandle"/>.
    /// </summary>
    internal readonly List<JobHandle>?[] Dependencies;

    /// <summary>
    /// An array that stores the unfished jobs of each <see cref="JobHandle"/>-
    /// </summary>
    internal readonly int[] UnfinishedJobs;

    /// <summary>
    /// An array that stores the <see cref="IJob"/> of each <see cref="JobHandle"/>.
    /// </summary>
    internal readonly IJob[] Jobs;

    /// <summary>
    /// The <see cref="JobHandlePool"/> with recycable ids.
    /// </summary>
    private readonly JobHandlePool _freeIds;

    /// <summary>
    /// Creates a new instance.
    /// </summary>
    internal JobHandleSoaPool()
    {
        const ushort MaxCount = ushort.MaxValue;
        _freeIds = new(MaxCount);
        Parent = new ushort[MaxCount];
        Dependencies = new List<JobHandle>?[MaxCount];
        UnfinishedJobs = new int[MaxCount];
        Jobs = new IJob[MaxCount];
    }

    /// <summary>
    /// Rents a new or pooled <see cref="JobHandle"/>.
    /// </summary>
    /// <param name="iJob">The <see cref="IJob"/>.</param>
    /// <returns>A new or pooled <see cref="JobHandle"/> instance.</returns>
    /// <exception cref="Exception">Throws when <see cref="IJob"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Throws if there no more handles available.</exception>
    internal JobHandle RentJobHandle(IJob iJob)
    {
        if (iJob == null)
        {
            throw new("Job cannot be null");
        }

        _freeIds.GetHandle(out var index);

        if (index == null)
        {
            throw new InvalidOperationException("No more handles available");
        }

        // Create handle
        return new()
        {
            Index = index.Value,
            Job = iJob,
            Parent = ushort.MaxValue,
            UnfinishedJobs = 1,
            Dependencies = null,
        };
    }

    /// <summary>
    /// Returns a new or pooled <see cref="JobHandle"/> to the pool.
    /// </summary>
    /// <param name="handle">The <see cref="JobHandle"/>.</param>
    public void ReturnHandle(JobHandle handle)
    {
        _freeIds.ReturnHandle(handle);
    }
}

/// <summary>
/// The <see cref="JobHandle"/> struct
/// is used to control and await a scheduled <see cref="IJob"/>.
/// <remarks>Size is exactly 64 bytes to fit perfectly into one default sized cacheline to reduce false sharing and be more efficient.</remarks>
/// </summary>
public struct JobHandle
{
    /// <summary>
    /// The <see cref="JobHandleSoaPool"/> for pooling and to reduce allocations.
    /// </summary>
    internal static readonly JobHandleSoaPool Pool = new();

    /// <summary>
    /// The index of this <see cref="JobHandle"/>, pointing towards its data inside the <see cref="Pool"/>.
    /// </summary>
    public ushort Index;

    /// <summary>
    /// Creates a new <see cref="JobHandle"/>.
    /// </summary>
    /// <param name="id">Its id.</param>
    public JobHandle(ushort id)
    {
        Index = id;
    }

    /// <summary>
    /// The associated <see cref="IJob"/> of this instance.
    /// </summary>
    public ref IJob Job
    {
        get => ref Pool.Jobs[Index];
    }

    /// <summary>
    /// The parent of this instance.
    /// </summary>
    public ref ushort Parent
    {
        get => ref Pool.Parent[Index];
    }

    /// <summary>
    /// The dependencies of this instance.
    /// </summary>
    public ref List<JobHandle>? Dependencies
    {
        get => ref Pool.Dependencies[Index];
    }

    /// <summary>
    /// Unfinished child jobs, in case that this one is a parent.
    /// </summary>
    public ref int UnfinishedJobs
    {
        get => ref Pool.UnfinishedJobs[Index];
    }

    /// <summary>
    /// Sets a <see cref="JobHandle"/> this instance depends on.
    /// </summary>
    /// <param name="toDependOn"></param>
    public void SetDependsOn(JobHandle toDependOn)
    {
        Interlocked.Increment(ref toDependOn.UnfinishedJobs);
        Parent = toDependOn.Index;
    }

    /// <summary>
    /// Returns whether this instance has <see cref="Dependencies"/>.
    /// </summary>
    /// <returns></returns>
    public bool HasDependencies()
    {
        return Dependencies is { Count: > 0 };
    }

    /// <summary>
    /// Returns a list of <see cref="Dependencies"/>.
    /// </summary>
    /// <returns></returns>
    public List<JobHandle> GetDependencies()
    {
        return Dependencies ??= [];
    }
}
