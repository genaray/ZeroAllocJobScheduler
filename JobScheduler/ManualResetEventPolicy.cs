using Microsoft.Extensions.ObjectPool;

namespace JobScheduler;

internal class ManualResetEventPolicy : IPooledObjectPolicy<ManualResetEvent>
{
    public ManualResetEvent Create()
    {
        var manualResetEvent = new ManualResetEvent(false);
        return manualResetEvent;
    }

    public bool Return(ManualResetEvent obj)
    {
        // We don't reset here, because there's a chance a Set() followed by a Reset() can not notify threads. Instead we must reset when acquiring.
        return true;
    }
}
