namespace JobScheduler.Test;

public class SleepJob : IJob
{
    public SleepJob(int time)
    {
        _time = time;
    }
    private readonly int _time;
    public int Result { get; private set; }
    public void Execute()
    {
        Thread.Sleep(_time);
        Result = 1;
    }
}
