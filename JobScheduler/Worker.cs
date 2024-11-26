using Schedulers.Utils;

namespace Schedulers;

/// <summary>
/// Represents a thread which has a <see cref="WorkStealingDeque{T}"/> and processes <see cref="JobHandle"/>s.
/// Steals <see cref="JobHandle"/>s from other workers if it has nothing more to do.
/// </summary>
internal class Worker
{
    private readonly int _workerId;
    private readonly Thread _thread;

    private readonly UnorderedThreadSafeQueue<JobHandle> _incomingQueue;
    private readonly WorkStealingDeque<JobHandle> _queue;

    private readonly JobScheduler _jobScheduler;
    private volatile CancellationTokenSource _cancellationToken;

    /// <summary>
    /// Creates a new <see cref="Worker"/>.
    /// </summary>
    /// <param name="jobScheduler">Its <see cref="JobScheduler"/>.</param>
    /// <param name="id">Its <see cref="id"/>.</param>
    public Worker(JobScheduler jobScheduler, int id)
    {
        _workerId = id;

        _incomingQueue = new UnorderedThreadSafeQueue<JobHandle>();
        _queue = new WorkStealingDeque<JobHandle>(32);

        _jobScheduler = jobScheduler;
        _cancellationToken = new CancellationTokenSource();

        _thread = new Thread(() => Run(_cancellationToken.Token));
    }

    /// <summary>
    /// Its <see cref="SingleProducerSingleConsumerQueue{T}"/> with <see cref="JobHandle"/>s which are transferred into the <see cref="Queue"/>.
    /// </summary>
    public UnorderedThreadSafeQueue<JobHandle> IncomingQueue
    {
        get => _incomingQueue;
    }

    /// <summary>
    /// Its <see cref="WorkStealingDeque{T}"/> with <see cref="JobHandle"/>s to process.
    /// </summary>
    public WorkStealingDeque<JobHandle> Queue
    {
        get => _queue;
    }

    /// <summary>
    /// Starts this instance.
    /// </summary>
    public void Start()
    {
        _thread.Start();
    }

    /// <summary>
    /// Stops this instance.
    /// </summary>
    public void Stop()
    {
        _cancellationToken.Cancel();
    }

    /// <summary>
    /// Runs this instance to process its <see cref="JobHandle"/>s.
    /// Steals from other <see cref="Worker"/>s if its own <see cref="_queue"/> is empty.
    /// </summary>
    /// <param name="token"></param>
    private void Run(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                // Pass jobs to the local queue
                while (_queue.Size() < 16 && _incomingQueue.TryDequeue(out var jobHandle))
                {
                    if (jobHandle == null) throw new InvalidOperationException("JobHandle is null");
                    _queue.PushBottom(jobHandle);
                }

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
            //Console.WriteLine("Operation was canceled");
        }
        finally
        {
            //Console.WriteLine("Worker thread is cleaning up and exiting");
        }
    }
}
