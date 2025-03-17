using System.Diagnostics.Contracts;

namespace Schedulers.Utils;

/// <summary>
/// Job that can be split into multiple jobs.
/// If you set _onlySingle to true, you can ignore RunVectorized
/// If you can make sure your job is always a multiple of the loop size, you can ignore RunSingle
/// </summary>
public interface IParallelJobProducer
{
    /// <summary>
    /// Use this by running a loop
    /// </summary>
    /// <param name="start">Index if the item you should start using</param>
    /// <param name="end">Exclusive end of the range</param>
    [Pure]
    public void RunVectorized(int start, int end)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// This is for the remaining items that are not vectorized
    /// </summary>
    /// <param name="index">The current item to be processed</param>
    [Pure]
    public void RunSingle(int index)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// A job producer that can be used to split a job into multiple jobs.
/// </summary>
/// <typeparam name="T">Type of the </typeparam>
/// <remarks>The reason we pass T instead of just IParallelJobProducer is because the compiler otherwise cannot inline it</remarks>
public class ParallelJobProducer<T> : IJob where T : struct, IParallelJobProducer
{
    private int _from;
    private readonly int _to;
    private readonly T _producer;
    private bool _shouldSplitWhenAvailable;
    private readonly JobScheduler _scheduler;
    private readonly JobHandle _selfHandle;
    private readonly bool _onlySingle;
    private readonly int _loopSize;

    /// <summary>
    /// Creates a new <see cref="ParallelJobProducer{T}"/>.
    /// </summary>
    /// <param name="to">Maximum to loop to</param>
    /// <param name="producer">The job to call</param>
    /// <param name="scheduler">The scheduler where the jobs should be put</param>
    /// <param name="loopSize">Size of the loop, useful for when you want to use vectorization</param>
    /// <param name="onlySingle">Makes sure you can ignore the end parameter in the IParallelJobProducer, and use start as index</param>
    public ParallelJobProducer(int to, T producer, JobScheduler scheduler, int loopSize = 16, bool onlySingle = false)
    {
        _from = 0;
        _to = to;
        _producer = producer;
        _shouldSplitWhenAvailable = true;
        _scheduler = scheduler;
        _onlySingle = onlySingle;
        _loopSize = loopSize;
        _selfHandle = _scheduler.Schedule(this);
        _scheduler.Flush(_selfHandle);
    }

    //Only used to spawn sub-jobs
    private ParallelJobProducer(T producer, JobScheduler scheduler, JobHandle parent, int start, int end, int loopSize, bool onlySingle)
    {
        _producer = producer;
        _shouldSplitWhenAvailable = false;
        _scheduler = scheduler;
        _from = start;
        _to = end;
        _loopSize = loopSize;
        _onlySingle = onlySingle;
        _selfHandle = _scheduler.Schedule(this, parent);
        _scheduler.Flush(_selfHandle);
    }

    /// <summary>
    /// Executes the job.
    /// If external thread overwrites the <see cref="_shouldSplitWhenAvailable"/> to true, it will split the job into multiple children.
    /// The current job will stop executing and the children will be scheduled.
    /// </summary>
    public void Execute()
    {
        if (_to - _from < 1)
        {
            throw new($"Invalid range from {_from} to {_to}");
        }

        var isSignificantRange = _to - _from > _loopSize * 4;

        // We only split if there is more than one child to split into, otherwise ignore the request
        // Also caching CheckAndSplit is pointless as this should be the last iteration
        if (isSignificantRange && !_onlySingle)
        {
            if (CheckAndSplit())
            {
                return;
            }

            for (; _from < _to - (_loopSize - 1); _from += _loopSize)
            {
                _producer.RunVectorized(_from, _from + _loopSize);
            }
        }

        for (; _from < _to; _from++)
        {
            if (CheckAndSplit())
            {
                return;
            }

            _producer.RunSingle(_from);
        }
    }

    private int CalculateChildrenToSplitInto()
    {
        const int ChildrenToSplitInto = 128; //This should be equal to the number of threads(or that times 2/3?) but for now it's just a constant
        var range = _to - _from;
        return range < ChildrenToSplitInto ? range : ChildrenToSplitInto;
    }

    private bool CheckAndSplit()
    {
        if (!_shouldSplitWhenAvailable || CalculateChildrenToSplitInto() <= 1)
        {
            return false;
        }

        Split();
        _shouldSplitWhenAvailable = false;
        return true;
    }

    private void Split()
    {
        var childrenToSplitInto = CalculateChildrenToSplitInto();
        for (var i = 0; i < childrenToSplitInto; i++)
        {
            var start = _from + ((_to - _from) * i / childrenToSplitInto);
            var end = _from + ((_to - _from) * (i + 1) / childrenToSplitInto);
            if (end - start < 1)
            {
                throw new($"Invalid range from {start} to {end}");
            }

            new ParallelJobProducer<T>(_producer, _scheduler, _selfHandle, start, end, _loopSize, _onlySingle);
        }
    }

    /// <summary>
    /// Returns the <see cref="JobHandle"/>.
    /// </summary>
    /// <returns></returns>
    public JobHandle GetHandle()
    {
        return _selfHandle;
    }
}
