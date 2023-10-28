namespace Schedulers.Deque;

/// <summary>
/// A <see cref="CircularArray{T}"/> as found in Chase and Lev pg. 3 [1]
/// </summary>
/// <remarks>
///     [1] Chase, D., &amp; Lev, Y. (2005). Dynamic circular work-stealing deque. Proceedings of the Seventeenth Annual ACM
///         Symposium on Parallelism in Algorithms and Architectures. https://doi.org/10.1145/1073970.1073974.
///         Retrieved October 17, 2023, from https://www.dre.vanderbilt.edu/~schmidt/PDF/work-stealing-dequeue.pdf.
/// </remarks>
/// <typeparam name="T"></typeparam>
internal class CircularArray<T>
{
    private readonly int _logSize;
    private readonly T[] _segment;

    /// <summary>
    /// Create a new <see cref="CircularArray{T}"/> with log_2(capacity) of at least <paramref name="logSize"/>.
    /// </summary>
    /// <param name="logSize"></param>
    // equivalent to CircularArray(int log_size) in Chase and Lev
    public CircularArray(int logSize)
    {
        _logSize = logSize;
        _segment = new T[Capacity];
    }

    /// <summary>
    /// Returns the current capacity of the array.
    /// </summary>
    // equivalent to size() in Chase and Lev
    public long Capacity
    {
        get => 1 << _logSize;
    }

    /// <summary>
    /// Get or set at an index of the array. If the index is greater than the array length, wraps around the array.
    /// </summary>
    /// <param name="i"></param>
    /// <returns></returns>
    public T this[long i]
    {
        // equivalent to get(long i) in Chase and Lev
        get => _segment[i % Capacity];
        // equivalent to put(long i, Object o) in Chase and Lev
        set => _segment[i % Capacity] = value;
    }

    /// <summary>
    ///     Grow the array by 2, copying the old array from a given <paramref name="b"/> and <paramref name="t"/>.
    /// </summary>
    /// <remarks>
    ///     The copied elements are guaranteed to map to the same indices from <paramref name="b"/> to
    ///     <paramref name="t"/>, no matter how large those indices are. Any other indices will not map properly, however.
    /// </remarks>
    /// <param name="t"></param>
    /// <param name="b"></param>
    /// <returns></returns>
    // equivalent to grow(long b, long t) in Chase and Lev
    public CircularArray<T> EnsureCapacity(long b, long t)
    {
        CircularArray<T> newArray = new(_logSize + 1);
        for (var i = t; i < b; i++)
        {
            newArray[i] = this[i];
        }

        return newArray;
    }
}
