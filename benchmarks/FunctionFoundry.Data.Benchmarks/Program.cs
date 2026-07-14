using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using FunctionFoundry.Data;

namespace FunctionFoundry.Data.Benchmarks;

internal static class Program
{
    private static void Main(string[] args) => _ = BenchmarkRunner.Run<DataBenchmarks>(args: args);
}

[MemoryDiagnoser]
public class DataBenchmarks
{
    private byte[] _left = null!;
    private byte[] _right = null!;
    private byte[] _base = null!;
    private byte[] _local = null!;
    private byte[] _remote = null!;

    [GlobalSetup]
    public void Setup()
    {
        _left = Encoding.UTF8.GetBytes("""{"items":[{"id":"a","v":1},{"id":"b","v":2}]}""");
        _right = Encoding.UTF8.GetBytes("""{"items":[{"id":"b","v":3},{"id":"a","v":1}]}""");
        _base = Encoding.UTF8.GetBytes("""{"a":1,"b":2,"c":3}""");
        _local = Encoding.UTF8.GetBytes("""{"a":1,"b":9,"c":3}""");
        _remote = Encoding.UTF8.GetBytes("""{"a":8,"b":2,"c":3}""");
    }

    [Benchmark]
    public StructuralTreeDiffResult StructuralDiff() => StructuralTreeDiff.Diff(_left, _right);

    [Benchmark]
    public ThreeWayMergeResult ThreeWayMerge() => ThreeWayMerge.Merge(_base, _local, _remote);
}
