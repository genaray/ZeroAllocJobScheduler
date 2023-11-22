using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Schedulers.Deque;

namespace Schedulers.Test;
[TestFixture]
internal class RangeWorkStealingDequeTests
{
    [Test]
    [TestCase(1024 * 756, 7, 8)]
    [TestCase(1024 * 756, 5, 8)]
    [TestCase(1024 * 756, 3, 8)]
    [TestCase(1024 * 756, 16, 8)]
    public void DequeProducesCorrectRanges(int size, int batchSize, int threadCount)
    {
        var random = new Random();

        var deque = new RangeWorkStealingDeque();
        deque.Set(0, size, batchSize);

        var threadIDs = new int[size];
        var increments = new int[size];

        var threads = new Thread[threadCount];
        var threadEvents = new ManualResetEvent[threadCount];

        threadEvents[0] = new(false);
        threads[0] = new Thread(() =>
        {
            while (deque.TryPopBottom(out var range) == RangeWorkStealingDeque.Status.Success)
            {
                for (var i = range.Start.Value; i < range.End.Value; i++)
                {
                    threadIDs[i] = Environment.CurrentManagedThreadId;
                    increments[i]++;
                }
            }

            threadEvents[0].Set();
        });

        for (var t = 1; t < threadCount; t++)
        {
            var tt = t;
            threadEvents[t] = new(false);
            threads[t] = new Thread(() =>
            {
                while (deque.TrySteal(out var range) != RangeWorkStealingDeque.Status.Empty)
                {
                    for (var i = range.Start.Value; i < range.End.Value; i++)
                    {
                        threadIDs[i] = Environment.CurrentManagedThreadId;
                        increments[i]++;
                    }
                }

                threadEvents[tt].Set();
            });
        }

        foreach (var thread in threads.OrderBy(x => random.Next()))
        {
            thread.Start();
        }

        WaitHandle.WaitAll(threadEvents);

        foreach (var i in increments)
        {
            Assert.That(i, Is.EqualTo(1));
        }

        // if this is failing, try increasing size (we need to simulate enough work so that all threads try)
        // this might also fail on CPUs with less than 8 cores due to no yielding?
        var distinctThreads = threadIDs.Distinct();
        Assert.That(distinctThreads.Count(), Is.EqualTo(threadCount));
    }
}
