namespace JobScheduler.Deque;

/// <summary>
///     A <see cref="RangeWorkStealingDeque"/> is an implementation of the Chase &amp; Lev Dynamic Circular Work-Stealing Deque [1]
///     specifically for the case of an array of deque of contiguous integers from [0, n). It is implemented specifically for the
///     <see cref="IJobParallelFor"/> case. It is a much lighter version than the full implementation of <see cref="WorkStealingDeque{T}"/>.
///     It is thread safe, lock-free, and concurrent, but with a caveat: It must have an owner process that exclusively calls
///     <see cref="TryPopBottom"/>. Any number of child stealers can call <see cref="TrySteal"/> concurrently.
/// </summary>
/// <remarks>
///     See <see cref="WorkStealingDeque{T}"/> for the canonical implementation.
/// </remarks>
internal class RangeWorkStealingDeque
{
    /// <summary>
    /// Unlike <see cref="WorkStealingDeque{T}"/>, we actually return a status depending on whether it's empty, because
    /// we're not using this with a Lin et al. algorithm, rather a much simpler algorithm that does need to know if it's
    /// finished or aborted due to contention.
    /// </summary>
    public enum Status
    {
        Empty,
        Abort,
        Success
    }

    // inclusive
    private long _top;
    // exclusive
    private long _bottom;

    private int _start;
    private int _end;
    private int _batchSize;

    /// <summary>
    /// Initializes an empty <see cref="RangeWorkStealingDeque"/>.
    /// </summary>
    public RangeWorkStealingDeque()
    {
        _top = 0;
        _bottom = 0;
    }

    /// <summary>
    /// Returns whether this <see cref="RangeWorkStealingDeque"/> is "empty," i.e. completed.
    /// </summary>
    public bool IsEmpty
    {
        get => Interlocked.Read(ref _top) >= Volatile.Read(ref _bottom);
    }

    /// <summary>
    /// Reset the state to the the initial range. Do not call while the work-stealing operation is in progress.
    /// </summary>
    /// <param name="start"></param>
    /// <param name="count"></param>
    /// <param name="batchSize"></param>
    public void Set(int start, int count, int batchSize)
    {
        var batches = count / batchSize;
        if (count % batchSize != 0)
        {
            batches++;
        }

        _top = 0;
        _bottom = batches;
        _start = start;
        _end = start + count;
        _batchSize = batchSize;
    }

    // Is oldVal equal to our current top? If so, we're good; exchange them and return true. If not, we went
    // out of date at some point. Don't exchange them, and return false.
    private bool CASTop(long oldVal, long newVal)
    {
        return Interlocked.CompareExchange(ref _top, newVal, oldVal) == oldVal;
    }

    /// <summary>
    /// Attempt a pop of some range from the bottom.
    /// </summary>
    /// <param name="range">The output range, valid only if the return value is equal to <see cref="Status.Success"/>.</param>
    /// <returns><see cref="Status.Empty"/> if the range is empty and we failed to pop. <see cref="Status.Success"/> if we succeeded in popping.</returns>
    public Status TryPopBottom(out Range range)
    {
        // we make no guarantees about what this even does if we return false
        range = default!;

        var b = Volatile.Read(ref _bottom);
        // we're popping, so decrement the bottom in advance.
        // Doing this in advance ensures that we "reserve space" in the size, so that even if someone steals,
        // they can't steal past this _bottom (their size would return less than 0, and they wouldn't steal). 
        // At the end of this method, if we need to adjust this (i.e. there ended up being nothing to steal, or less than amount to steal)
        // we resolve this.
        b--;
        Volatile.Write(ref _bottom, b);

        // check the size...
        var t = Interlocked.Read(ref _top);
        var size = b - t;

        // if we were empty before, we're still empty, so we just make sure we're canon (top == bottom).
        if (size < 0)
        {
            Volatile.Write(ref _bottom, t);
            return Status.Empty;
        }

        var popped = MakeRange(b);
        // if we're not empty even after the pop, we're good to go.
        // This means we can pop even without the expensive CAS!
        if (size > 0)
        {
            range = popped;
            return Status.Success;
        }

        // But what if the pop makes it empty? We need to check to see if we won a race with any Stealers.

        // If this returns false, we lost the race, because our t value became out of date. (Steal did this operation instead).
        // Note that incrementing _top if we're empty is totally OK because top > bottom is always empty.
        if (!CASTop(t, t + 1))
        {
            // Reset to empty canon. Remember, our t is out of date by one, so after this _top == _bottom
            Volatile.Write(ref _bottom, t + 1);
            return Status.Empty;
        }

        // We won the race! Return the full remaining range and reset to empty canon.
        Volatile.Write(ref _bottom, t + 1);
        range = popped;
        return Status.Success;
    }

    /// <summary>
    /// Attempt a steal of some range from the top.
    /// </summary>
    /// <param name="range">The output range, valid only if the return value is equal to <see cref="Status.Success"/>.</param>
    /// <returns><see cref="Status.Empty"/> if the range is empty and we failed to steal. <see cref="Status.Success"/> if we succeeded in stealing.
    /// <see cref="Status.Abort"/> if contention occurred and we should try again.</returns>
    public Status TrySteal(out Range range)
    {
        // We make no guarantees about what this even does if we return false
        range = default!;
        var t = Interlocked.Read(ref _top);
        var b = Volatile.Read(ref _bottom);
        var size = b - t;

        // If we're empty, don't even try.
        if (size <= 0)
        {
            return Status.Empty;
        }

        // We know we're not empty, so give it a shot:
        var stolen = MakeRange(t);

        // Check for a race between TryPopBottom, other stealers, and us.
        // If we successfully increment the top (decreasing the size of the deque), we win.
        if (!CASTop(t, t + 1))
        {
            // We lose and return false since someone else got to the item before we did.
            return Status.Abort;
        }

        // We won!
        range = stolen;
        return Status.Success;
    }

    private Range MakeRange(long index)
    {
        // For example, if this is index 1, and we started at 52 and had a batch size of 12...
        // We're starting at batch 2 of the whole index at 64
        var start = ((int)index * _batchSize) + _start;
        // And we're ending 1 batch later at 80
        var end = start + _batchSize;
        // However, we don't want to return too much, so truncate by the original end.
        // This is how we deal with remainders.
        end = Math.Min(end, _end);
        return new Range(start, end);
    }
}
