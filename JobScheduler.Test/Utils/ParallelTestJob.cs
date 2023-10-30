using System.Diagnostics.CodeAnalysis;

namespace JobScheduler.Test.Utils;

/// <summary>
/// A <see cref="IJobParallelFor"/> that stores an internal array that can be tested against for validation.
/// </summary>
public class ParallelTestJob : IJobParallelFor
{
    public int ThreadCount { get; }
    public int BatchSize { get; }

    public bool FinalizerRun { get; private set; } = false;

    private readonly int[] _array;

    public ParallelTestJob(int batchSize, int threadCount, int expectedSize)
    {
        _array = new int[expectedSize];
        ThreadCount = threadCount;
        BatchSize = batchSize;
    }

    public void Execute(int index)
    {
        _array[index]++;
    }

    public void Finish()
    {
        FinalizerRun = true;
        AssertIsTotallyComplete();
    }

    [SuppressMessage("Assertion", "NUnit2045:Use Assert.Multiple", Justification = "<Pending>")]
    public void AssertIsTotallyIncomplete()
    {
        SearchArray(out var foundZeroValue, out var foundOneValue, out var foundOtherValue);
        Assert.That(foundZeroValue, Is.EqualTo(_array.Length));
        Assert.That(foundOneValue, Is.EqualTo(0));
        Assert.That(foundOtherValue, Is.EqualTo(0));
    }

    [SuppressMessage("Assertion", "NUnit2045:Use Assert.Multiple", Justification = "<Pending>")]
    public void AssertIsTotallyComplete()
    {
        SearchArray(out var foundZeroValue, out var foundOneValue, out var foundOtherValue);
        Assert.That(foundZeroValue, Is.EqualTo(0));
        Assert.That(foundOneValue, Is.EqualTo(_array.Length));
        Assert.That(foundOtherValue, Is.EqualTo(0));
        Assert.That(FinalizerRun, Is.True);
    }

    private void SearchArray(out int foundZeroValue, out int foundOneValue, out int foundOtherValue)
    {
        foundZeroValue = 0;
        foundOneValue = 0;
        foundOtherValue = 0;
        foreach (var item in _array)
        {
            switch (item)
            {
                case 0:
                    foundZeroValue++;
                    break;
                case 1:
                    foundOneValue++;
                    break;
                default:
                    foundOtherValue++;
                    break;
            }
        }
    }

    public void Reset()
    {
        for (var i = 0; i < _array.Length; i++)
        {
            _array[i] = 0;
        }

        FinalizerRun = false;
    }
}
