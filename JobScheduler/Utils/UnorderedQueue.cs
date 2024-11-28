using System.Collections.Concurrent;

namespace Schedulers.Utils;

public class UnorderedThreadSafeQueue<T>
{
    private ConcurrentQueue<T> _queue = new();

    public UnorderedThreadSafeQueue()
    {
        for (var i = 0; i < 1024; i++)
        {
            _queue.Enqueue(default!);
        }

        for (var i = 0; i < 1024; i++)
        {
            _queue.TryDequeue(out _);
        }
    }

    internal bool TryDequeue(out T item)
    {
        return _queue.TryDequeue(out item);
    }

    internal bool TryEnqueue(T item)
    {
        _queue.Enqueue(item);
        return true;
    }
}
