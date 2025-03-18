namespace Schedulers.Utils;

/// <summary>
/// This <see cref="JobHandlePool"/> class
/// acts as a pool for <see cref="JobHandle"/> ids.
/// </summary>
internal class JobHandlePool
{
    private readonly ushort[] _handles;
    private readonly bool[] _isFree;
    private readonly Queue<ushort> _freeHandles;
    private int _returnedHandles;

    /// <summary>
    /// Creates a new instance.
    /// </summary>
    /// <param name="size">Its initial size.</param>
    public JobHandlePool(int size)
    {
        _freeHandles = new(size);
        _handles = new ushort[size];
        _isFree = new bool[size];

        for (ushort i = 0; i < size; i++)
        {
            _handles[i] = i;
            _freeHandles.Enqueue(i);
            _isFree[i] = true;
        }
    }

    /// <summary>
    /// Rents a new handle.
    /// </summary>
    /// <param name="handle">The rented handle.</param>
    /// <remarks>Not intrinsically thread safe so we lock. Assumption is that the user won't generate handles on other threads</remarks>
    /// <returns>True or false.</returns>
    internal bool RentHandle(out ushort? handle)
    {
        lock (this)
        {
            if (_freeHandles.Count == 0)
            {
                Clear();
            }

            if (_freeHandles.Count == 0)
            {
                handle = null;
                return false;
            }

            var index = _freeHandles.Dequeue();
            _isFree[index] = false;
            handle = _handles[index];
            return true;
        }
    }

    /// <summary>
    /// Returns a rented handle.
    /// </summary>
    /// <param name="handle">The initial rented handle.</param>
    /// <remarks>Thread safe.</remarks>
    internal void ReturnHandle(JobHandle handle)
    {
        _isFree[handle.Index] = true;
        _returnedHandles++; // We don't care about thread safety here
    }


    /// <summary>
    /// Clears this instance.
    /// </summary>
    private void Clear()
    {
        // Prevent cleanup if no handles were returned
        // This is a guard to prevent cleanup every time someone tries to get a handle, in a tight loop
        if (_returnedHandles == 0)
        {
            return;
        }

        _freeHandles.Clear();
        for (ushort i = 0; i < _isFree.Length; i++)
        {
            if (_isFree[i])
            {
                _freeHandles.Enqueue(i);
            }
        }

        _returnedHandles = 0;
    }
}
