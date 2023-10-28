namespace Schedulers;

/// <summary>
/// A super-fast pseudo-random number generator.
/// </summary>
internal class XorshiftRandom
{
    private uint _x;

    public XorshiftRandom()
    {
        _x = (uint)DateTime.Now.Ticks; // Initialize with a non-constant seed (e.g., current time)
    }

    public void Seed(uint seed)
    {
        _x = seed;
    }

    public int Next(int min, int max)
    {
        if (min >= max)
        {
            throw new ArgumentOutOfRangeException($"{nameof(min)} must be less than max");
        }

        var range = max - min;

        if (range <= 0)
        {
            throw new ArgumentOutOfRangeException($"The range ({nameof(max)} - {nameof(min)}) must be greater than 0");
        }

        _x ^= _x << 13;
        _x ^= _x >> 17;
        _x ^= _x << 5;

        return min + (int)(_x % (uint)range);
    }
}
