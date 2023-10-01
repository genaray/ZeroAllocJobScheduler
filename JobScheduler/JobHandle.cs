using System.Runtime.CompilerServices;

namespace JobScheduler;

/// <summary>
/// Wraps a <see cref="WaitHandle"/> variant, and is used to control and await a scheduled <see cref="IJob"/>.
/// </summary>
public readonly struct JobHandle
{
    /// <summary>
    /// The <see cref="ManualResetEvent"/> (WaitHandle variant).
    /// </summary>
    internal readonly ManualResetEvent _event;

    /// <summary>
    /// A bool indicating whether the thread itself or the user returns the handle to the pool.
    /// </summary>
    internal readonly bool _poolOnComplete;

    internal JobHandle(ManualResetEvent @event, bool poolOnComplete)
    {
        _event = @event;
        _poolOnComplete = poolOnComplete;
    }

    /// <summary>
    /// Notifies <see cref="JobHandle"/>, sets it signal.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Notify()
    {
        _event.Set();
    }

    /// <summary>
    /// Waits for the <see cref="JobHandle"/> to complete.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if the job was scheduled with automatic poolOnComplete enabled.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Complete()
    {
        if (_poolOnComplete) throw new InvalidOperationException(
            $"Cannot manually {nameof(Complete)} a {nameof(JobHandle)} that was scheduled with poolOnComplete enabled! " +
            $"It might have automatically completed and returned to the pool to be reused, already.");
        _event.WaitOne();
    }

    /// <summary>
    /// Returns the <see cref="JobHandle"/> to its object pool for later reuse.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if the job was scheduled with automatic poolOnComplete enabled.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Return()
    {
        if (_poolOnComplete) throw new InvalidOperationException(
            $"Cannot manually {nameof(Return)} a {nameof(JobHandle)} that was scheduled with poolOnComplete enabled! " +
            $"It might have automatically completed and returned to the pool to be reused, already.");
        if (_event != null) JobScheduler.ManualResetEventPool.Return(_event);
    }

    /// <summary>
    /// Waits and blocks the calling thread until all <see cref="JobHandle"/>s are completed.
    /// </summary>
    /// <remarks>This is equivalent to calling <see cref="Complete()"/> on each <see cref="JobHandle"/> individually.</remarks>
    /// <exception cref="InvalidOperationException">Thrown if any job was scheduled with automatic poolOnComplete enabled.</exception>
    /// <param name="handles">The handles to complete.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Complete(JobHandle[] handles)
    {
        for (var index = 0; index < handles.Length; index++)
        {
            ref var handle = ref handles[index];
            handle.Complete();
        }
    }

    /// <summary>
    /// Waits and blocks the calling thread until all <see cref="JobHandle"/>s are completed.
    /// </summary>
    /// <remarks>This is equivalent to calling <see cref="Complete()"/> on each <see cref="JobHandle"/> individually.</remarks>
    /// <exception cref="InvalidOperationException">Thrown if any job was scheduled with automatic poolOnComplete enabled.</exception>
    /// <param name="handles">The handles to complete.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Complete(IList<JobHandle> handles)
    {
        for (var index = 0; index < handles.Count; index++)
        {
            var handle = handles[index];
            handle.Complete();
        }
    }

    /// <summary>
    /// Returns and recycles all given handles. 
    /// </summary>
    /// <param name="handles">The handles to recycle.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Return(JobHandle[] handles)
    {
        for (var index = 0; index < handles.Length; index++)
        {
            ref var handle = ref handles[index];
            handle.Return();
        }
    }

    /// <summary>
    /// Returns and recycles all given handles. 
    /// </summary>
    /// <param name="handles">The handles to recycle.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Return(IList<JobHandle> handles)
    {
        for (var index = 0; index < handles.Count; index++)
        {
            var handle = handles[index];
            handle.Return();
        }
    }
}
