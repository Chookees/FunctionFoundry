using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using FunctionFoundry.Networking;

namespace FunctionFoundry.Networking.Benchmarks;

internal static class Program
{
    private static void Main(string[] args) => _ = BenchmarkRunner.Run<NetworkingBenchmarks>(args: args);
}

[MemoryDiagnoser]
public class NetworkingBenchmarks
{
    private TransferPlan _plan = null!;
    private MirrorSelector _selector = null!;

    [GlobalSetup]
    public void Setup()
    {
        _plan = TransferPlan.Create(16 * 1024 * 1024, 256 * 1024);
        _selector = new MirrorSelector();
        _selector.Record(new MirrorObservation("a", 30, 2_000_000, true, true));
        _selector.Record(new MirrorObservation("b", 45, 1_500_000, true, true));
    }

    [Benchmark]
    public TransferPlanValidationResult ValidatePlan() => _plan.Validate();

    [Benchmark]
    public string SelectMirror() => _selector.Select(["a", "b"]);
}
