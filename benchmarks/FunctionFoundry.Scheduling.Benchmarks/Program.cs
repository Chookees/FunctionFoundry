using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using FunctionFoundry.Scheduling;

namespace FunctionFoundry.Scheduling.Benchmarks;

internal static class Program
{
    private static void Main(string[] args) => _ = BenchmarkRunner.Run<SchedulingBenchmarks>(args: args);
}

[MemoryDiagnoser]
public class SchedulingBenchmarks
{
    private List<TimeInterval> _intervals = null!;
    private CriticalPathScheduler _scheduler = null!;
    private ScheduleTask[] _tasks = null!;

    [GlobalSetup]
    public void Setup()
    {
        DateTimeOffset start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        _intervals = new List<TimeInterval>(512);
        for (int i = 0; i < 512; i++)
        {
            _intervals.Add(new(start.AddHours(i), start.AddHours(i + 1)));
        }

        _scheduler = new CriticalPathScheduler();
        _tasks = Enumerable.Range(0, 64)
            .Select(i => new ScheduleTask($"t{i}", 1, i == 0 ? [] : [$"t{i - 1}"]))
            .ToArray();
    }

    [Benchmark]
    public IReadOnlyList<TimeInterval> NormalizeIntervals() => IntervalSetAlgebra.Normalize(_intervals);

    [Benchmark]
    public CriticalPathScheduleResult ScheduleChain() => _scheduler.Schedule(_tasks);
}
