namespace Schedulers.Utils;
internal class SingleProducerSingleConsumerQueue<T>
{
    private volatile CircularArray<T> _buffer;
    private long _bottom;
    private long _top;

    public SingleProducerSingleConsumerQueue(int initialLogSize = 10)
    {
        _buffer = new CircularArray<T>(initialLogSize);
        _bottom = 0;
        _top = 0;
    }

    public bool TryEnqueue(T item)
    {
        while (true)
        {
            var bottom = Interlocked.Read(ref _bottom);
            var top = Interlocked.Read(ref _top);
            var size = bottom - top;

            // Prüfe Buffer-Kapazität
            if (size >= _buffer.Capacity)
            {
                var currentBuffer = _buffer;
                var newBuffer = currentBuffer.EnsureCapacity(bottom, top);

                if (Interlocked.CompareExchange(ref _buffer, newBuffer, currentBuffer) != currentBuffer)
                {
                    continue;
                }
            }

            if (Interlocked.CompareExchange(ref _bottom, bottom + 1, bottom) == bottom)
            {
                _buffer[bottom] = item;
                return true;
            }

            Thread.SpinWait(1);
        }
    }

    public bool TryDequeue(out T result)
    {
        result = default;

        while (true)
        {
            var bottom = Interlocked.Read(ref _bottom);
            var top = Interlocked.Read(ref _top);

            // Queue leer
            if (bottom <= top)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _top, top + 1, top) == top)
            {
                result = _buffer[top];
                _buffer[top] = default;
                return true;
            }

            Thread.SpinWait(1);
        }
    }

    public long Count
    {
        get
        {
            var bottom = Interlocked.Read(ref _bottom);
            var top = Interlocked.Read(ref _top);
            return Math.Max(0, bottom - top);
        }
    }
}
