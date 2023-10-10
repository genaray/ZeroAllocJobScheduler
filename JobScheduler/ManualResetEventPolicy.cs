using Microsoft.Extensions.ObjectPool;

namespace JobScheduler;

/// <summary>
///     The <see cref="ManualResetEventPolicy"/> class
///     is a <see cref="IPooledObjectPolicy{T}"/> that pools <see cref="ManualResetEvent"/>s to avoid garbage. 
/// </summary>
internal class ManualResetEventPolicy : IPooledObjectPolicy<ManualResetEvent>
{
    /// <inheritdoc/>
    public ManualResetEvent Create()
    {
        var manualResetEvent = new ManualResetEvent(false);
        return manualResetEvent;
    }
    
    /// <inheritdoc/>
    public bool Return(ManualResetEvent obj)
    {
        // We don't reset here, because there's a chance a Set() followed by a Reset() can not notify threads. Instead we must reset when acquiring.
        return true;
    }
}
