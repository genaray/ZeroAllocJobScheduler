using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace JobScheduler; 

/// <summary>
/// Wraps a <see cref="WaitHandle"/> Variant and is used to control / await a scheduled <see cref="IJob"/>.
/// </summary>
public readonly struct JobHandle {

    /// <summary>
    /// The ManualResetEvent - Waithandle.
    /// </summary>
    internal readonly ManualResetEvent _event;
    
    /// <summary>
    /// A bool indicating whether the thread itself or tue user returns the handle to the pool. 
    /// </summary>
    internal readonly bool _poolOnComplete;

    public JobHandle(ManualResetEvent @event, bool @poolOnComplete) {
        _event = @event;
        _poolOnComplete = @poolOnComplete;
    }

    /// <summary>
    /// Notifies <see cref="JobHandle"/>, sets it signal.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Notify() {
        _event.Set();
    }
    
    /// <summary>
    /// Waits for the <see cref="JobHandle"/> completion.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Complete() {
        
        if (_poolOnComplete) throw new Exception("PoolOnComplete was set on JobHandle, therefore it returns automatically to the pool and you can not wait for its completion anymore since its handle might have been already reused.");
        _event.WaitOne();
    }

    /// <summary>
    /// Returns/Pools the <see cref="JobHandle"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Return() {

        if (_poolOnComplete) throw new Exception("PoolOnComplete was set on JobHandle, therefore it returns automatically to the pool. Do not call Return on such a handle !");
        if(_event != null) JobScheduler.ManualResetEventPool.Return(_event);
    }

    /// <summary>
    /// Waits and blocks the calling thread till all <see cref="JobHandle"/>'s are completed.
    /// </summary>
    /// <param name="handles">The handles to wait till they are completed.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Complete(JobHandle[] handles) {

        for (var index = 0; index < handles.Length; index++) {
            ref var handle = ref handles[index];
            handle.Complete();
        }
    }
    
    /// <summary>
    /// Waits and blocks the calling thread till all <see cref="JobHandle"/>'s are completed.
    /// </summary>
    /// <param name="handles">The handles to wait till they are completed.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Complete(IList<JobHandle> handles) {

        for (var index = 0; index < handles.Count; index++) {
            var handle = handles[index];
            handle.Complete();
        }
    }
    
    /// <summary>
    /// Returns and recycles all handles. 
    /// </summary>
    /// <param name="handles">The handles to recycle.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Return(JobHandle[] handles) {

        for (var index = 0; index < handles.Length; index++) {
            ref var handle = ref handles[index];
            handle.Return();
        }
    }
    
    /// <summary>
    /// Returns and recycles all handles. 
    /// </summary>
    /// <param name="handles">The handles to recycle.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Return(IList<JobHandle> handles) {

        for (var index = 0; index < handles.Count; index++) {
            var handle = handles[index];
            handle.Return();
        }
    }
}
