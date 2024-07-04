using Schedulers.Utils;

namespace Schedulers;

internal struct Worker
{
    private readonly JobScheduler _jobScheduler;

    private readonly Thread _thread;
    private readonly WorkStealingDeque<JobHandle> _queue;
    private volatile bool _running;
    private readonly int _workerId;

    public Worker(JobScheduler jobScheduler, int id)
    {
        _jobScheduler = jobScheduler;
        _workerId = id;
        _queue = new WorkStealingDeque<JobHandle>(32);
        _running = true;
        _thread = new Thread(Run);
    }

    public WorkStealingDeque<JobHandle> Queue
    {
        get => _queue;
    }

    public void Start()
    {
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        if (!_thread.Join(500))
        {
            _thread.Interrupt();
        }
    }

    private void Run()
    {
        try
        {
            while (_running)
            {
                // Process job in own queue
                var exists = _queue.TryPopBottom(out var job);
                if (exists)
                {
                    job._job.Execute();
                    _jobScheduler.Finish(job);
                }
                else
                {
                    // Try to steal job from different queue
                    for (var i = 0; i < _jobScheduler.Queues.Count; i++)
                    {
                        if (i == _workerId)
                        {
                            continue;
                        }

                        //Console.WriteLine($"Test: {_jobScheduler} and {_jobScheduler.Queues} and {job}");
                        exists = _jobScheduler.Queues[i].TrySteal(out job);
                        if (!exists)
                        {
                            continue;
                        }

                        job._job.Execute();
                        _jobScheduler.Finish(job);
                        break;
                    }

                    if (!exists)
                    {
                        // No work found, yield to give other threads a chance
                        Thread.Yield();
                    }
                }
            }
        }
        catch(Exception e)
        {
            throw e;
        }
    }
}
