namespace Arch.Benchmarks;

public class Benchmark
{
    private static void Main(string[] args)
    {
        // Use: dotnet run -c Release --framework net7.0 -- --job short --filter *BenchmarkClass1*
        BenchmarkSwitcher.FromAssembly(typeof(Benchmark).Assembly).Run(args);
    }
}
