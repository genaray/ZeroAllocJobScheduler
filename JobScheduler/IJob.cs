namespace Schedulers;

/// <summary>
///     The <see cref="IJob"/> interface.
///     represents a job which can outsource tasks to the <see cref="JobScheduler"/>.
/// </summary>
public interface IJob
{
    /// <summary>
    /// Gets called by a thread to execute the job logic.
    /// </summary>
    void Execute();
}
