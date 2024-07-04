using Schedulers.Utils;

namespace Schedulers;

internal class Worker
{

    private readonly int _workerId;
    private readonly Thread _thread;
    private readonly WorkStealingDeque<JobHandle> _queue;

    private readonly JobScheduler _jobScheduler;
    private volatile CancellationTokenSource _cancellationToken;

    public Worker(JobScheduler jobScheduler, int id)
    {
        _workerId = id;
        _queue = new WorkStealingDeque<JobHandle>(32);

        _jobScheduler = jobScheduler;
        _cancellationToken = new CancellationTokenSource();

        _thread = new Thread(() => Run(_cancellationToken.Token));
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
        _cancellationToken.Cancel();
    }

    private void Run(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
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
        catch (OperationCanceledException)
        {
            Console.WriteLine("Operation was canceled");
        }
        finally
        {
            Console.WriteLine("Worker thread is cleaning up and exiting");
        }
    }
}
