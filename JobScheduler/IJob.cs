namespace Schedulers;

/// <summary>
/// The <see cref="IJob"/> interface
/// represents a job that can be packed into the queue and is executed by a thread at a specific time.
/// </summary>
public interface IJob
{
    /// <summary>
    /// Is called by a thread at a certain time to execute the job.
    /// </summary>
    void Execute();
}
