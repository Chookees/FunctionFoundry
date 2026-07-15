namespace FunctionFoundry.Scheduling.Tests;

public sealed class CriticalPathSchedulerExtraTests
{
    [Fact]
    public void Empty_graph_is_valid_with_empty_critical_path()
    {
        var scheduler = new CriticalPathScheduler();
        CriticalPathScheduleResult result = scheduler.Schedule([]);
        Assert.Empty(result.CriticalPath);
        Assert.Equal(0, result.ProjectDuration);
    }

    [Fact]
    public void Unknown_dependency_is_reported_by_validate()
    {
        var scheduler = new CriticalPathScheduler();
        ScheduleTask[] tasks = [new("a", 1, ["missing"])];
        IReadOnlyList<ScheduleValidationError> errors = scheduler.Validate(tasks);
        Assert.Contains(errors, static e => e.Message.Contains("unknown task", StringComparison.Ordinal));
    }
}
