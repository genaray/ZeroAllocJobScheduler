namespace JobScheduler.Test.Utils;

/// <summary>
/// A test job that increments <see cref="Result"/> when executed.
/// </summary>
internal class TestJob : IJob
{
    public int Result { get; private set; } = 0;
    public virtual void Execute()
    {
        Result++;
    }
}
