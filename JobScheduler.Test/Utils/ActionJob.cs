using JobScheduler.Test.Utils;

namespace JobScheduler.Test;

/// <summary>
/// A <see cref="TestJob"/> that runs an arbitrary action before incrementing its <see cref="TestJob.Result"/>
/// </summary>
internal class ActionJob : TestJob
{
    private readonly Action _action;

    public ActionJob(Action action)
    {
        _action = action;
    }

    public override void Execute()
    {
        _action.Invoke();
        base.Execute();
    }
}
