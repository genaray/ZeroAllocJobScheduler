using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JobScheduler.Benchmarks.Utils.Job;

/// <summary>
/// A <see cref="IJobParallelFor"/> that stores an internal array that can be tested against for validation.
/// </summary>
public class ParallelTestJob : IJobParallelFor
{
    public int ThreadCount { get; }
    public int BatchSize
    {
        get => 0;
    }

    private readonly int[] _array;

    public ParallelTestJob(int threadCount, int expectedSize)
    {
        _array = new int[expectedSize];
        ThreadCount = threadCount;
    }

    public void Execute(int index)
    {
        _array[index]++;
    }

    public bool IsTotallyIncomplete
    {
        get
        {
            foreach (var item in _array)
            {
                if (item != 0)
                {
                    return false;
                }
            }

            return true;
        }
    }

    public bool IsTotallyComplete
    {
        get
        {
            foreach (var item in _array)
            {
                if (item != 1)
                {
                    return false;
                }
            }

            return true;
        }
    }

    public void Reset()
    {
        for (var i = 0; i < _array.Length; i++)
        {
            _array[i] = 0;
        }
    }
}
