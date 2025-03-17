namespace Schedulers.Utils;

public static class JobSchedulerExtensions
{

    /// <summary>
    ///     Transfers a collection of <see cref="JobHandle"/> instances to the <see cref="Worker"/> so that they can be executed.
    /// </summary>
    /// <param name="jobs">A span of <see cref="JobHandle"/> instances to be distributed.</param>
    public static void Flush(this JobScheduler jobScheduler, Span<JobHandle> jobs)
    {
        foreach (ref var job in jobs)
        {
            jobScheduler.Flush(job);
        }
    }

    /// <summary>
    ///     Waits until all submitted <see cref="JobHandle"/>s are finished and processes unfinished jobs on the main thread in the meantime.
    /// </summary>
    /// <param name="jobScheduler">The <see cref="JobScheduler"/>.</param>
    /// <param name="jobs">An array of <see cref="JobHandle"/>s to wait for.</param>
    public static void Wait(this JobScheduler jobScheduler, params JobHandle[] jobs)
    {
        jobScheduler.Wait(jobs.AsSpan());
    }

    /// <summary>
    ///     Waits until all submitted <see cref="JobHandle"/>s are finished and processes unfinished jobs on the main thread in the meantime.
    /// </summary>
    /// <param name="jobScheduler">The <see cref="JobScheduler"/>.</param>
    /// <param name="jobs">A <see cref="Span{T}"/> of <see cref="JobHandle"/>s to wait for.</param>
    public static void Wait(this JobScheduler jobScheduler, Span<JobHandle> jobs)
    {
        while (true)
        {
            // Check if jobs are finished
            var allJobsFinished = true;
            for (var i = 0; i < jobs.Length; i++)
            {
                if (jobs[i].UnfinishedJobs > 0)
                {
                    allJobsFinished = false;
                    break;
                }
            }

            if (allJobsFinished)
            {
                break;
            }

            // Steal jobs and process them on the main.
            for (var i = 0; i < jobScheduler.Workers.Count; i++)
            {
                var nextJob = jobScheduler.Workers[i].Queue.TrySteal(out var stolenJob);
                if (!nextJob)
                {
                    continue;
                }

                stolenJob.Job.Execute();
                jobScheduler.Finish(stolenJob);
            }
        }
    }
}
