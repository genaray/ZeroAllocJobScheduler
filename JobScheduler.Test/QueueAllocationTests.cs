using JobScheduler.Test.Utils.CustomConstraints;
using System.Collections.Concurrent;

namespace JobScheduler.Test;

[TestFixture]
internal class QueueAllocationTests
{
    private struct BigStruct
    {
#pragma warning disable CS0649 // Field 'QueueAllocationTests.BigStruct.l1' is never assigned to, and will always have its default value 0
        internal long l1;
        internal long l2;
        internal long l3;
        internal long l4;
        internal long l5;
        internal long l6;
        internal long l7;
        internal long l8;
        internal long l9;
        internal long l10;
        internal long l11;
        internal long l12;
        internal long l13;
        internal long l14;
        internal long l15;
        internal long l16;
#pragma warning restore CS0649 // Field 'QueueAllocationTests.BigStruct.l1' is never assigned to, and will always have its default value 0
    }

    // test 
    [Test]
    [TestCase(32)]
    [TestCase(128)]
    public void ConcurrentQueueAllocationCache(int n)
    {
        Queue<BigStruct> cacheQueue = new();

        // cache
        for (var i = 0; i < n; i++)
        {
            cacheQueue.Enqueue(default);
        }

        ConcurrentQueue<BigStruct> queue = new(cacheQueue);

        while (!queue.IsEmpty)
        {
            queue.TryDequeue(out var _);
        }

        for (var i = 0; i < n; i++)
        {
            Assert.That(() => { queue.Enqueue(default); }, Is.Not.AllocatingMemory());
        }

        while (!queue.IsEmpty)
        {
            Assert.That(() => { queue.TryDequeue(out var _); }, Is.Not.AllocatingMemory());
        }
    }
}
