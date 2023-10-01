using System.Runtime.CompilerServices;

namespace JobScheduler.Extensions;

/// <summary>
/// Extensions for <see cref="IJob"/>
/// </summary>
public static class IJobExtensions
{
    /// <summary>
    /// Schedules the job to the global <see cref="JobScheduler"/> instance, which must be initialized already.
    /// </summary>
    /// <param name="jobData">The job itself</param>
    /// <param name="poolOnComplete">If set, the worker thread will automatically return the handle to the pool after it completes. 
    /// The user should not call <see cref="JobHandle.Return()"/> or <see cref="JobHandle.Complete()"/> on it!</param>
    /// <typeparam name="T">The type of <see cref="IJob"/></typeparam>
    /// <returns>The <see cref="JobHandle"/> used to wait for the job.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static JobHandle Schedule<T>(this T jobData, bool poolOnComplete = false) where T : IJob
    {
        var job = JobScheduler.Instance.Schedule(jobData, poolOnComplete);
        return job;
    }
}