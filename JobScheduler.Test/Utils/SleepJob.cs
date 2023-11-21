namespace Schedulers.Test.Utils;

/// <summary>
/// A <see cref="TestJob"/> that sleeps before incrementing its result.
/// </summary>
internal class SleepJob : TestJob
{
    public SleepJob(int time)
    {
        _time = time;
    }
    private readonly int _time;
    public override void Execute()
    {
        Thread.Sleep(_time);
        base.Execute();
    }
}
