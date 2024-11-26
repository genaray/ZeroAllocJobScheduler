using Schedulers.Utils;

/// <summary>
///     A <see cref="WorkStealingDeque{T}"/> is an implementation of the Chase &amp; Lev Dynamic Circular Work-Stealing Deque [1]
///     It is thread safe, lock-free, and concurrent, but with a caveat: It must have an owner process that exclusively calls
///     <see cref="TryPopBottom(out T)"/> and <see cref="PushBottom(T)"/>. Any number of child stealers can call
///     <see cref="TrySteal(out T)"/> concurrently.
/// </summary>
/// <remarks>
///     While Chase &amp; Lev provide several options for memory management, we choose to let resizes discard of the additional
///     memory through GC. This is because we don't expect to frequently grow, or to shrink at all, given our API.
///     [1] Chase, D., &amp; Lev, Y. (2005). Dynamic circular work-stealing deque. Proceedings of the Seventeenth Annual ACM
///         Symposium on Parallelism in Algorithms and Architectures. ⟨10.1145/1073970.1073974⟩.
///         Retrieved October 17, 2023, from https://www.dre.vanderbilt.edu/~schmidt/PDF/work-stealing-dequeue.pdf.
///     [2] Nhat Minh Lê, Antoniu Pop, Albert Cohen, Francesco Zappa Nardelli. Correct and Efficient Work-Stealing for Weak Memory
///         Models. PPoPP '13 - Proceedings of the 18th ACM SIGPLAN symposium on Principles and practice of parallel programming,
///         Feb 2013, Shenzhen, China. pp.69-80, ⟨10.1145/2442516.2442524⟩. ⟨hal-00802885⟩. Retrieved October 17, 2023 from
///         https://hal.science/hal-00786679/.
/// </remarks>
/// <typeparam name="T"></typeparam>
internal class WorkStealingDeque<T>
{
    // This class operates on two fundamental insights:
    //
    //    1. _top never decrements
    //    2. _bottom is only ever modified by the owning process.
    //
    // This means that modifying _bottom is "free," i.e. while pushing/popping to the bottom (our process's "owned half") we
    // don't have to worry about concurrence issues.
    //
    // This also means that when concurrence issues do come up, either with stealing, or with popping from an almost-empty queue,
    // we can always rely on CAS-incrementing _top to tell us whether a race-condition occured.
    //
    // I.e. if our _top is equal to the actual top, we can CAS, and therefore we won the race. Otherwise, someone else (or even multiple
    // others) incremented _top first, and we lost the race, so we give up our operation.
    //
    // Other than that, it's a very standard CircularArray-based Deque, where the top and bottom pointers move around depending on the
    // operation. The owner pushes and pops from the bottom, moving the bottom around wherever, and the stealer pops from the top, exclusively
    // raising the top value and never EVER lowering it.
    //
    // This isn't a complete Deque, because the operation PushTop would force us to decrement Top (which would violate our ability to use CAS
    // to act as a "version" of the top).
    //
    // A valid array can be visualized like this:
    //     t        b
    // [_  x  y  z  _  _]
    // t = _top pointer
    // b = _bottom pointer
    // x, y, z = valid elements in the Deque
    //
    // This array is used as the starting point for all the following operation examples.
    //
    // Pushing a to the bottom looks like this (incrementing bottom to make the queue bigger; ONLY thread-safe from the owning thread):
    //     t           b
    // [_  x  y  z  a  _]
    //
    // Stealing* looks like this (incrementing top to make the queue smaller; thread-safe):
    //        t     b
    // [_  _  y  z  _  _]  => stole x!
    //
    // Popping* z from the bottom looks like this (decrementing bottom to make the queue smaller; ONLY thread-safe from the owning thread):
    //     t     b
    // [_  x  y  _  _  _] => popped z!
    //
    // *These methods might conflict with each other, which is why we check _top in both methods after we do most of the operation to
    // ensure it's still valid.

    // This must be specifically atomically accessed/modified because c# doesn't support volatile longs.
    // Use Volatile.Read/Volatile.Write, which is OK because _bottom is only written from one processor.
    private long _bottom = 0;

    // The same should apply to _top, but it doesn't. I don't know why.
    // The original algorithm uses volatile to read _top and CAS to write _top.
    // However, when we do the same, multiple steal operations accidentally grab the same variable, sometimes.
    // (VERY VERY VERY occasionally. Only really observable through high-stress benchmarks in my experience.)
    // (If you want to test, run on a Debug configuration with new DebugInProcessConfig(), because the already-complete
    // job validation is only run on Debug.)
    // From research, the only difference between Interlocked.Read and Volatile.Read is that Volatile might access
    // a slightly old version of the variable, which our code should be able to deal with since CASTop checks that.
    // But it doesn't work.
    // If anyone has ideas, speak up. This could be caused by...
    //     * A weird edge-case in .NET
    //     * An oversight in the original algorithm (but what?)
    //     * A misunderstanding on my part of how this works
    // Instead, for now, use Interlocked.Read to access _top no matter what.
    // (It's not an issue with the volatile modifier vs. the Volatile class; volatile modifier on ints fails as well)
    // Nhat et al. [2] might have some insight into this, hidden inside the C++ code, but it's far more low-level than
    // we get here, and therefore of limited use.
    private long _top = 0;

    // According to section 2.3, we keep a cache of the last top value, to guess at the size of the Deque
    // so we don't have to look at _top every time.
    // We init to 0 because the cached size will be b - 0, and we know that, since t < b, that will give a
    // larger-than-life value.
    private long _lastTopValue = 0;

    private volatile CircularArray<T> _activeArray;

    /// <summary>
    /// Create a new <see cref="WorkStealingDeque{T}"/> with capacity of at least <paramref name="capacity"/>.
    /// </summary>
    /// <param name="capacity"></param>
    public WorkStealingDeque(int capacity)
    {
        _activeArray = new CircularArray<T>((int)Math.Ceiling(Math.Log(capacity, 2)));
    }

    // Is oldVal equal to our current top? If so, we're good; exchange them and return true. If not, we went
    // out of date at some point. Don't exchange them, and return false.
    private bool CASTop(long oldVal, long newVal)
    {
        return Interlocked.CompareExchange(ref _top, newVal, oldVal) == oldVal;
    }

    /// <summary>
    ///     Push an item to the bottom of the <see cref="WorkStealingDeque{T}"/>.
    /// </summary>
    /// <remarks>
    ///     This method must ONLY be called by the deque's owning process, ever!
    ///     It is not concurrent with itself, only with <see cref="TrySteal(out T)"/>
    /// </remarks>
    /// <param name="item">The item to add.</param>
    public void PushBottom(T item)
    {
        var b = Volatile.Read(ref _bottom);
        var a = _activeArray;

        // we use the cached value per section 2.3, to avoid a blocking top read
        var sizeUpperBound = b - _lastTopValue;
        if (sizeUpperBound >= a.Capacity - 1)
        {
            // we think we might need a resize, but we're not sure, so access the volatile variable.
            var t = Interlocked.Read(ref _top);
            _lastTopValue = t; // cache it
            var actualSize = b - t;
            if (actualSize >= a.Capacity - 1)
            {
                a = a.EnsureCapacity(b, t);
                _activeArray = a;
            }
        }

        a[b] = item;

        // Incrementing the bottom is always safe, because Steal never changes bottom.
        // Steal can only ever change top.
        Volatile.Write(ref _bottom, b + 1);
    }

    /// <summary>
    ///     Attempt to pop an item from the bottom of the <see cref="WorkStealingDeque{T}"/>.
    /// </summary>
    /// <remarks>
    ///     This method must ONLY be called by the deque's owning process, ever!
    ///     It is not concurrent with itself, only with <see cref="TrySteal(out T)"/>
    /// </remarks>
    /// <param name="item">Set to the popped item if success. If no success, undefined.</param>
    /// <returns>True if we popped successfully and therefore <paramref name="item"/> contains useful data.</returns>
    public bool TryPopBottom(out T item)
    {
        // we make no guarantees about what this even does if we return false
        item = default!;

        var b = Volatile.Read(ref _bottom);
        var a = _activeArray;

        // we're popping, so decrement the bottom in advance.
        // Doing this in advance ensures that we "reserve space" in the size, so that even if someone steals,
        // they can't steal past this _bottom (their size would return 0, and they wouldn't steal).
        // At the end of this method, if we need to adjust this (i.e. there ended up being nothing to steal)
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
            return false;
        }

        // if we're not empty even after the pop, we're good to go.
        // This means we can pop even without the expensive CAS!
        var popped = a[b];
        if (size > 0)
        {
            item = popped;
            return true;
        }

        // But what if the pop makes it empty? We need to check to see if we won a race with any Stealers.
        // If this returns false, we lost the race, because our t value became out of date. (Steal did this operation instead).
        // Note that incrementing _top if we're empty is totally OK because top > bottom is always empty.
        if (!CASTop(t, t + 1))
        {
            // We lost the race, and the queue is now empty.
            // Reset to empty canon:
            Volatile.Write(ref _bottom, t + 1);
            return false;
        }

        // We won the race! Return the item and reset to canon.
        Volatile.Write(ref _bottom, t + 1);
        item = popped;
        return true;
    }

    /// <summary>
    ///     Attempt to steal an item from the top of the <see cref="WorkStealingDeque{T}"/>.
    /// </summary>
    /// <remarks>
    ///     Unlike <see cref="PushBottom"/> and <see cref="TryPopBottom"/>, this method can be called from any thread
    ///     at any time, and it is guaranteed to be concurrently compatible with all other methods including itself.
    /// </remarks>
    /// <param name="item">Set to the stolen item if success. If no success, undefined.</param>
    /// <returns>True if we stole successfully and therefore <paramref name="item"/> contains useful data.</returns>
    public bool TrySteal(out T item)
    {
        // We make no guarantees about what this even does if we return false
        item = default!;
        var t = Interlocked.Read(ref _top);
        var b = Volatile.Read(ref _bottom);
        // In case the array gets resized, we grab a reference to the old one.
        // That will protect it from the GC while we do this method.
        // Since the array (in this implementation) only gets resized on push, the top index must be where we expect.
        // Unless the top index changes: in which case we got out-raced and we'll exit later.
        var a = _activeArray;
        var size = b - t;

        // If we're empty, don't even try.
        if (size <= 0)
        {
            return false;
        }
        // We know we're not empty, so give it a shot:
        var stolen = a[t];

        // Check for a race between TryPopBottom, other stealers, and us.
        // If we successfully increment the top (decreasing the size of the deque), we win.
        if (!CASTop(t, t + 1))
        {
            // We lose and return false since someone else got to the item before we did.
            return false;
        }

        // We won!
        item = stolen;
        return true;
    }

    public int Size()
    {
        return (int)(Volatile.Read(ref _bottom) - Interlocked.Read(ref _top));
    }
}
