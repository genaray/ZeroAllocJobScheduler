using System;
using System.Threading;
using Microsoft.Extensions.ObjectPool;

namespace JobScheduler;

internal class ManualResetEventPolicy : IPooledObjectPolicy<ManualResetEvent> {

    public ManualResetEvent Create() {

        var manualResetEvent = new ManualResetEvent(false);
        return manualResetEvent;
    }

    public bool Return(ManualResetEvent obj) {
        return true;
    }
}
