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
            // Round Robin distribution
            var workerIndex = jobScheduler.NextWorkerIndex;
            jobScheduler.Workers[workerIndex].Queue.PushBottom(job);
            jobScheduler.NextWorkerIndex = (jobScheduler.NextWorkerIndex + 1) % jobScheduler.Workers.Count;
        }
    }

    /// <summary>
    ///     Waits until all submitted <see cref="JobHandle"/>s are finished and processes unfinished jobs on the main thread in the meantime.
    /// </summary>
    /// <param name="jobScheduler">The <see cref="JobScheduler"/>.</param>
    /// <param name="jobs">An array of <see cref="JobHandle"/>s to wait for.</param>
    public static void Wait(this JobScheduler jobScheduler, params JobHandle[] jobs)
    {
        while (true)
        {
            // Check if jobs are finished
            var allJobsFinished = true;
            for (var i = 0; i < jobs.Length; i++)
            {
                if (jobs[i]._unfinishedJobs > 0)
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

                stolenJob._job.Execute();
                jobScheduler.Finish(stolenJob);
            }
        }
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
                if (jobs[i]._unfinishedJobs > 0)
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

                stolenJob._job.Execute();
                jobScheduler.Finish(stolenJob);
            }
        }
    }
}
