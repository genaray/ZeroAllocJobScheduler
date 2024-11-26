namespace Schedulers.Utils;

public class SimpleQueue<T>
{
    public T[] data;
    public int enqueueIndex;
    public int dequeueIndex;

    public SimpleQueue(int size)
    {
        data = new T[size];
        enqueueIndex = 0;
        dequeueIndex = 0;
    }

    public bool TryEnqueue(T item)
    {
        if (enqueueIndex >= data.Length)
        {
            return false;
        }

        data[enqueueIndex] = item;
        //very important, because if we write the index before the data, the consumer could read try to read null data
        Volatile.Write(ref enqueueIndex, enqueueIndex + 1);
        return true;
    }

    public bool TryDequeue(out T item)
    {
        if (dequeueIndex >= enqueueIndex)
        {
            item = default!;
            return false;
        }

        item = data[dequeueIndex++];
        data[dequeueIndex - 1] = default!;
        return true;
    }
}

public class UnorderedThreadSafeQueue<T>
{
    private const int Size = 128;
    private SimpleQueue<T> _queueA = new(Size);
    private SimpleQueue<T> _queueB = new(Size);

    internal bool TryDequeue(out T item)
    {
        var res = _queueB.TryDequeue(out item);
        if (!res && _queueA.enqueueIndex > 0)
        {
            SwapBuffers();
            return _queueB.TryDequeue(out item);
        }

        return res;
    }

    internal bool TryEnqueue(T item)
    {
        return _queueA.TryEnqueue(item);
    }

    private void SwapBuffers()
    {
        Volatile.Write(ref _queueB.enqueueIndex, 0);
        Volatile.Write(ref _queueA.dequeueIndex, 0);
        (_queueA, _queueB) = (_queueB, _queueA);
    }
}
