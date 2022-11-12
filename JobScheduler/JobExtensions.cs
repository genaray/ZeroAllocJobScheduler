using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace JobScheduler.Extensions; 

/// <summary>
/// Extension for <see cref="IJob"/>
/// </summary>
public static class IJobExtensions {
    
    /// <summary>
    /// Schedules the job to the global <see cref="JobScheduler"/> ( mus have been initialized somewhere before ).
    /// </summary>
    /// <param name="jobData">The job itself</param>
    /// <typeparam name="T">The type</typeparam>
    /// <returns>The <see cref="JobHandle"/> used to wait for the job.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static JobHandle Schedule<T>(this T jobData, bool poolOnComplete = false) where T : IJob {
        
        var job = JobScheduler.Instance.Schedule(jobData, poolOnComplete);
        return job;
    }
}