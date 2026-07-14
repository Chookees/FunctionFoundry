using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using FunctionFoundry.Resilience;

namespace FunctionFoundry.Resilience.Benchmarks;

internal static class Program
{
    private static void Main(string[] args) => _ = BenchmarkRunner.Run<ResilienceBenchmarks>(args: args);
}

[MemoryDiagnoser]
public sealed class ResilienceBenchmarks : IDisposable
{
    private AdaptiveConcurrencyController _controller = null!;
    private HedgedExecution _hedged = null!;
    private ExecutionBudget _budget = null!;

    [GlobalSetup]
    public void Setup()
    {
        _controller = new AdaptiveConcurrencyController();
        _hedged = new HedgedExecution();
        _budget = ExecutionBudget.Create(TimeSpan.FromMinutes(1));
    }

    [GlobalCleanup]
    public void Cleanup() => Dispose();

    [Benchmark]
    public AdaptiveConcurrencySnapshot Snapshot() => _controller.GetSnapshot();

    [Benchmark]
    public async Task<int> HedgedExecute() =>
        await _hedged.ExecuteAsync(_ => Task.FromResult(1), isIdempotent: true);

    [Benchmark]
    public bool ReserveRelease()
    {
        bool reserved = _budget.TryReserve(TimeSpan.FromMilliseconds(10), out BudgetReservation reservation);
        if (reserved)
        {
            _budget.Release(reservation, TimeSpan.FromMilliseconds(5));
        }

        return reserved;
    }

    public void Dispose()
    {
        _controller.Dispose();
        _budget.Dispose();
        GC.SuppressFinalize(this);
    }
}
