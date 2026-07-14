namespace FunctionFoundry.Scheduling.Tests;

public sealed class CriticalPathSchedulerTests
{
    [Fact]
    public void Schedule_computes_critical_path_and_slack()
    {
        var scheduler = new CriticalPathScheduler();
        ScheduleTask[] tasks =
        [
            new("a", 3, []),
            new("b", 2, ["a"]),
            new("c", 4, ["a"]),
            new("d", 1, ["b", "c"]),
        ];
        CriticalPathScheduleResult result = scheduler.Schedule(tasks);
        Assert.Equal(8, result.ProjectDuration);
        Assert.Contains("a", result.CriticalPath);
        Assert.Contains("c", result.CriticalPath);
        Assert.Contains("d", result.CriticalPath);
        Assert.True(result.IsOptimal);
    }

    [Fact]
    public void Validate_reports_cycle_with_path()
    {
        var scheduler = new CriticalPathScheduler();
        ScheduleTask[] tasks =
        [
            new("a", 1, ["c"]),
            new("b", 1, ["a"]),
            new("c", 1, ["b"]),
        ];
        IReadOnlyList<ScheduleValidationError> errors = scheduler.Validate(tasks);
        Assert.Contains(errors, static e => e.Cycle is not null && e.Cycle.Count >= 3);
    }

    [Fact]
    public void Resource_heuristic_marks_schedule_non_optimal()
    {
        var scheduler = new CriticalPathScheduler(new CriticalPathSchedulerOptions
        {
            ResourceDemandByTaskId = new Dictionary<string, int> { ["a"] = 3 },
            ResourceCapacity = 1,
        });
        ScheduleTask[] tasks = [new("a", 2, [])];
        CriticalPathScheduleResult result = scheduler.Schedule(tasks);
        Assert.True(result.UsedResourceHeuristic);
        Assert.False(result.IsOptimal);
        Assert.Equal(4, result.ProjectDuration);
    }
}
