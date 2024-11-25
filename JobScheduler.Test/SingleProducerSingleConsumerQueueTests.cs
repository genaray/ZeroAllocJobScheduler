using System.Collections.Concurrent;
using NUnit.Framework;
using Schedulers.Utils;

namespace Schedulers.Test;

[TestFixture]
public class LockFreeSPSCQueueTests
{
    [Test]
    public void BasicEnqueueDequeue_ShouldWorkCorrectly()
    {
        // Arrange
        var queue = new SingleProducerSingleConsumerQueue<int>();

        // Act & Assert
        for (var i = 0; i < 100; i++)
        {
            Assert.That(queue.TryEnqueue(i), Is.True, $"Failed to enqueue item {i}");
        }

        Assert.That(queue.Count, Is.EqualTo(100), "Queue count is incorrect after enqueuing");

        for (var i = 0; i < 100; i++)
        {
            Assert.That(queue.TryDequeue(out var value), Is.True, $"Failed to dequeue item {i}");
            Assert.That(value, Is.EqualTo(i), $"Dequeued value {value} does not match expected {i}");
        }

        Assert.That(queue.Count, Is.EqualTo(0), "Queue should be empty after dequeuing all items");
    }

    [Test]
    public void BufferExpansion_ShouldHandleLargeNumberOfItems()
    {
        // Arrange
        var queue = new SingleProducerSingleConsumerQueue<int>(2); // Small initial buffer
        const int itemCount = 10_000;

        // Act
        for (var i = 0; i < itemCount; i++)
        {
            queue.TryEnqueue(i);
        }

        // Assert
        Assert.That(queue.Count, Is.EqualTo(itemCount), "Queue count is incorrect after enqueuing large number of items");

        for (var i = 0; i < itemCount; i++)
        {
            Assert.That(queue.TryDequeue(out var value), Is.True, $"Failed to dequeue item {i}");
            Assert.That(value, Is.EqualTo(i), $"Dequeued value {value} does not match expected {i}");
        }
    }

    /*
    [Test]
    public void BufferExpansion_ShouldRecycleSlots()
    {
        // Arrange
        var queue = new SingleProducerSingleConsumerQueue<int>();

        // Act & Assert
        for (var i = 0; i < 1024; i++)
        {
            Assert.That(queue.TryEnqueue(i), Is.True, $"Failed to enqueue item {i}");
        }

        Assert.That(queue.Count, Is.EqualTo(1024), "Queue count is incorrect after enqueuing");

        for (var i = 0; i < 1024; i++)
        {
            Assert.That(queue.TryDequeue(out var value), Is.True, $"Failed to dequeue item {i}");
            Assert.That(value, Is.EqualTo(i), $"Dequeued value {value} does not match expected {i}");
        }

        Assert.That(queue.Count, Is.EqualTo(0), "Queue should be empty after dequeuing all items");

        queue.TryEnqueue(-1);
    }*/

    [Test]
    public void ConcurrentProducerConsumer_ShouldHandleHighConcurrency()
    {
        // Arrange
        var queue = new SingleProducerSingleConsumerQueue<int>();
        const int itemCount = 1_000_000;
        var producedItems = new ConcurrentBag<int>();
        var consumedItems = new ConcurrentBag<int>();

        // Synchronisationsmechanismus
        var producerComplete = new ManualResetEventSlim(false);
        var consumerComplete = new ManualResetEventSlim(false);

        // Act
        var producerTask = Task.Run(() =>
        {
            try
            {
                for (var i = 0; i < itemCount; i++)
                {
                    while (!queue.TryEnqueue(i))
                    {
                        Thread.SpinWait(1);
                    }
                    producedItems.Add(i);
                }
            }
            finally
            {
                producerComplete.Set();
            }
        });

        var consumerTask = Task.Run(() =>
        {
            try
            {
                int consumedCount = 0;
                // Warte, bis Producer fertig ist
                producerComplete.Wait();

                while (consumedCount < itemCount)
                {
                    if (queue.TryDequeue(out int value))
                    {
                        consumedItems.Add(value);
                        consumedCount++;
                    }
                    else if (consumedCount >= itemCount)
                    {
                        break;
                    }
                    else
                    {
                        Thread.SpinWait(1);
                    }
                }
            }
            finally
            {
                consumerComplete.Set();
            }
        });

        // Warte auf Abschluss
        producerComplete.Wait();
        consumerComplete.Wait();

        // Assert
        Assert.AreEqual(0, queue.Count, "Queue should be empty after producer-consumer test");
        Assert.That(producedItems.Count, Is.EqualTo(itemCount), "Not all items were produced");
        Assert.That(consumedItems.Count, Is.EqualTo(itemCount), "Not all items were consumed");

        // Sortiere und vergleiche
        var sortedProducedItems = producedItems.OrderBy(x => x).ToList();
        var sortedConsumedItems = consumedItems.OrderBy(x => x).ToList();

        for (var i = 0; i < itemCount; i++)
        {
            Assert.That(sortedConsumedItems[i], Is.EqualTo(sortedProducedItems[i]),
                $"Mismatch at index {i}: Produced {sortedProducedItems[i]} != Consumed {sortedConsumedItems[i]}");
        }
    }

    [Test]
    public void StressTest_MultipleEnqueueDequeueOperations()
    {
        // Arrange
        var queue = new SingleProducerSingleConsumerQueue<int>();
        const int iterations = 100_000;
        var producedSum = 0;
        var consumedSum = 0;

        // Act
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            var testValue = iteration;
            producedSum += testValue;

            queue.TryEnqueue(testValue);

            if (queue.TryDequeue(out var dequeuedValue))
            {
                consumedSum += dequeuedValue;
            }
        }

        // Consume remaining items
        while (queue.TryDequeue(out var remainingValue))
        {
            consumedSum += remainingValue;
        }

        // Assert
        Assert.That(consumedSum, Is.EqualTo(producedSum), "Sum of produced and consumed items should match");
    }

    [Test]
    public void ThreadStarvation_ShouldNotOccur()
    {
        // Arrange
        var queue = new SingleProducerSingleConsumerQueue<int>();
        const int threadCount = 10;
        const int itemsPerThread = 10_000;
        var producedItems = new ConcurrentBag<int>();
        var consumedItems = new ConcurrentBag<int>();
        var startBarrier = new Barrier(threadCount * 2);

        // Act
        var tasks = new Task[threadCount * 2];

        // Producers
        for (var t = 0; t < threadCount; t++)
        {
            var threadIndex = t;
            tasks[t] = Task.Run(() =>
            {
                startBarrier.SignalAndWait();
                for (var i = 0; i < itemsPerThread; i++)
                {
                    var item = threadIndex * itemsPerThread + i;
                    queue.TryEnqueue(item);
                    producedItems.Add(item);
                }
            });
        }

        // Consumers
        for (var t = 0; t < threadCount; t++)
        {
            tasks[threadCount + t] = Task.Run(() =>
            {
                startBarrier.SignalAndWait();
                var consumedCount = 0;
                while (consumedCount < itemsPerThread)
                {
                    if (queue.TryDequeue(out var value))
                    {
                        consumedItems.Add(value);
                        consumedCount++;
                    }
                }
            });
        }

        // Wait for all tasks
        Task.WaitAll(tasks);

        // Assert
        Assert.That(queue.Count, Is.EqualTo(0), "Queue should be empty after thread starvation test");
        Assert.That(producedItems.Count, Is.EqualTo(threadCount * itemsPerThread), "Not all items were produced");
        Assert.That(consumedItems.Count, Is.EqualTo(threadCount * itemsPerThread), "Not all items were consumed");
    }
}
