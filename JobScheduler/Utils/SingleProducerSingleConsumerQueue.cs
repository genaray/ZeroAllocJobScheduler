using System.Collections.Concurrent;

namespace Schedulers.Utils;
internal class SingleProducerSingleConsumerQueue<T>
{
    private readonly ConcurrentQueue<T> _queue = new();

    public SingleProducerSingleConsumerQueue(int capacity)
    {
        for (var i = 0; i < capacity; i++)
        {
            _queue.Enqueue(default!);
        }

        for (var i = 0; i < capacity; i++)
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
