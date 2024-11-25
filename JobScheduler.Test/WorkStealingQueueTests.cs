using Schedulers.Utils;

namespace Schedulers.Test;

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

[TestFixture]
public class WorkStealingQueueTests
{
    [Test]
    public void SingleThreaded_PushAndPop_WorksCorrectly()
    {
        var queue = new WorkStealingDeque<int>(5);

        // Push items
        for (int i = 0; i < 100; i++)
        {
            queue.PushBottom(i);
        }

        // Pop items and verify LIFO order
        for (int i = 99; i >= 0; i--)
        {
            Assert.IsTrue(queue.TryPopBottom(out int item));
            Assert.AreEqual(i, item);
        }

        // Queue should be empty
        Assert.IsFalse(queue.TryPopBottom(out _));
    }

    [Test]
    public void MultiThreaded_PushAndSteal_WorksCorrectly()
    {
        var queue = new WorkStealingDeque<int>(5);
        var results = new ConcurrentBag<int>();
        var processedItems = new ConcurrentDictionary<int, int>();
        const int itemCount = 10000;
        const int stealerCount = 3;

        // Producer task
        var producer = Task.Run(() =>
        {
            for (int i = 0; i < itemCount; i++)
            {
                queue.PushBottom(i);
                Thread.SpinWait(10); // Simulate some work
            }
        });

        // Stealer tasks
        var stealers = Enumerable.Range(0, stealerCount).Select(_ => Task.Run(() =>
        {
            while (results.Count < itemCount)
            {
                if (queue.TrySteal(out int item))
                {
                    results.Add(item);
                    if (!processedItems.TryAdd(item, 1))
                    {
                        Assert.Fail($"Item {item} was processed more than once!");
                    }
                }
                else
                {
                    Thread.SpinWait(10);
                }
            }
        })).ToArray();

        // Wait for all tasks to complete
        Task.WaitAll(new[] { producer }.Concat(stealers).ToArray());

        // Verify results
        Assert.AreEqual(itemCount, results.Count, "Not all items were processed");
        Assert.AreEqual(itemCount, processedItems.Count, "Some items were processed multiple times");
        CollectionAssert.AreEquivalent(
            Enumerable.Range(0, itemCount),
            results,
            "Not all expected items were found in the results"
        );
    }

    [Test]
    public void EdgeCase_EmptyQueue_StealAndPopReturnFalse()
    {
        var queue = new WorkStealingDeque<int>(5);

        // Try steal from empty queue
        Assert.IsFalse(queue.TrySteal(out _));

        // Try pop from empty queue
        Assert.IsFalse(queue.TryPopBottom(out _));

        // Push one item
        queue.PushBottom(1);

        // Two threads try to get the same item
        var tasks = new[]
        {
            Task.Run(() => queue.TryPopBottom(out _)),
            Task.Run(() => queue.TrySteal(out _))
        };

        var results = Task.WhenAll(tasks).Result;

        // Only one thread should succeed
        Assert.AreEqual(1, results.Count(r => r), "Only one thread should get the item");
    }
}
