using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using FunctionFoundry.Distributed;

namespace FunctionFoundry.Distributed.Benchmarks;

internal static class Program
{
    private static void Main(string[] args) => _ = BenchmarkRunner.Run<DistributedBenchmarks>(args: args);
}

[MemoryDiagnoser]
public class DistributedBenchmarks
{
    private WeightedRendezvousHasher _hasher = null!;
    private WeightedNode[] _nodes = null!;
    private byte[] _key = null!;
    private VectorClock _clock = null!;

    [GlobalSetup]
    public void Setup()
    {
        _hasher = new WeightedRendezvousHasher();
        _nodes =
        [
            new("n1", 1),
            new("n2", 2),
            new("n3", 1),
            new("n4", 3),
        ];
        _key = "benchmark-key"u8.ToArray();
        _clock = new VectorClock();
        _clock.Increment("a");
        _clock.Increment("b");
    }

    [Benchmark]
    public WeightedNode RendezvousSelect() => _hasher.Select(_key, _nodes);

    [Benchmark]
    public string VectorClockSerialize() => _clock.Serialize();
}
