namespace JobScheduler.Benchmarks;

/// <summary>
/// Increments a simple counter as the work;
/// </summary>
[MemoryDiagnoser]
public class ParallelForBenchmarkMatrix : ParallelForBenchmark
{
    private readonly int _dim = 700;
    private float[] _matrixA = null!;
    private float[] _matrixB = null!;
    private float[] _matrixC = null!;

    public override int Size { get => _dim * _dim; }
    public override int Waves { get => 1; }
    protected override int BatchSize { get => 64; }

    protected override void Init()
    {
        if (Math.Sqrt(Size) % 1 != 0)
        {
            throw new Exception($"Size must be a perfect square for {nameof(ParallelForBenchmarkMatrix)} to work!");
        }

        _matrixA = new float[Size];
        _matrixB = new float[Size];
        _matrixC = new float[Size];

        var random = new Random();

        for (var i = 0; i < Size; i++)
        {
            _matrixA[i] = random.Next(0, 255);
            _matrixB[i] = random.Next(0, 255);
        }
    }

    // run for each int in Size
    protected override void Work(int index)
    {
        var row = index / _dim;
        var col = index % _dim;
        float sum = 0;

        for (var k = 0; k < _dim; k++)
        {
            sum += _matrixA[(row * _dim) + k] * _matrixB[(k * _dim) + col];
        }

        _matrixC[index] = sum;
    }

    protected override bool Validate()
    {
        var expectedResult = new float[Size];
        for (var i = 0; i < _dim; i++)
        {
            for (var j = 0; j < _dim; j++)
            {
                float sum = 0;
                for (var k = 0; k < _dim; k++)
                {
                    sum += _matrixA[(i * _dim) + k] * _matrixB[(k * _dim) + j];
                }

                expectedResult[(i * _dim) + j] = sum;
            }
        }

        for (var i = 0; i < Size; i++)
        {
            if (_matrixC[i] != expectedResult[i])
            {
                return false;
            }
        }

        return true;
    }
}
