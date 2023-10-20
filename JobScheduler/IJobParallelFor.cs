namespace JobScheduler;

/// <summary>
///     Represents a special job that, when scheduled, calls <see cref="Execute(int)"/> with every value of <c>index</c>.
/// </summary>
/// <remarks>
///     While useful, this version shouldn't be overused. For trivial work, a for-loop is going to be better. It isn't always worth the
///     overhead to run a full parallel job. Always profile your code before going more parallel, especially with very small work sizes!
/// </remarks>
public interface IJobParallelFor
{
    /// <summary>
    ///     The amount of threads to simultaneously run this job on, or 0 to use <see cref="JobScheduler.Config.ThreadCount"/>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This counts for <see cref="JobScheduler.Config.MaxExpectedConcurrentJobs"/>. So, if you schedule a parallel job on a
    ///         <see cref="JobScheduler"/> with <c><see cref="JobScheduler.Config.MaxExpectedConcurrentJobs"/> = 32</c>, and set
    ///         <see cref="ThreadCount"/> to <c>16</c> for a 16-core processor, just one scheduling of this <see cref="IJobParallelFor"/>
    ///         will use up half your <see cref="JobScheduler.Config.MaxExpectedConcurrentJobs"/>. It is recommended to keep this to a
    ///         minimum, if possible: often times, for smaller amounts of values, performance gains with many threads will be negligible.
    ///     </para>
    ///     <para>
    ///         This does not, however, ensure that <see cref="ThreadCount"/> threads will actually be used. If other threads are busy,
    ///         the active threads might finish the whole thing before they can get a chance.
    ///     </para>
    /// </remarks>
    public int ThreadCount { get; }

    // TODO: Once we do work stealing for parallel jobs, a batch size parameter should go here.

    /// <summary>
    ///     Implement this method to define the execution behavior, just like a normal <see cref="IJob"/>.
    ///     The <see cref="Execute"/> method will be called by threads for every <c>int</c> in <c>[0, n]</c> where <c>n</c>n is the
    ///     value passed in during <see cref="JobScheduler.Schedule(IJobParallelFor, int, JobHandle?)"/>.
    /// </summary>
    /// <param name="index"></param>
    public void Execute(int index);
}
