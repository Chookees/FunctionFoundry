namespace FunctionFoundry.Scheduling.Tests;

public sealed class BusinessCalendarTests
{
    [Fact]
    public void Add_working_duration_skips_weekend_and_split_hours()
    {
        var calendar = new BusinessCalendar(new BusinessCalendarOptions { TimeZone = TimeZoneInfo.Utc });
        DateTimeOffset friday = new(2026, 1, 2, 16, 0, 0, TimeSpan.Zero);
        BusinessCalendarResult result = calendar.AddWorkingDuration(friday, TimeSpan.FromHours(2));
        Assert.True(result.Steps.Count >= 2);
        Assert.Equal(DayOfWeek.Monday, result.Instant.DayOfWeek);
    }

    [Fact]
    public void Subtract_working_duration_explain_path_records_steps()
    {
        var calendar = new BusinessCalendar(new BusinessCalendarOptions
        {
            TimeZone = TimeZoneInfo.Utc,
            Closures = [new CalendarClosure(new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 1, 6, 0, 0, 0, TimeSpan.Zero), "holiday")],
        });
        DateTimeOffset end = new(2026, 1, 7, 10, 0, 0, TimeSpan.Zero);
        BusinessCalendarResult result = calendar.SubtractWorkingDuration(end, TimeSpan.FromHours(1));
        Assert.Contains(result.Steps, static s => s.Description.Contains("holiday", StringComparison.OrdinalIgnoreCase) || s.Description.Contains("Start", StringComparison.Ordinal));
        Assert.True(result.Instant < end);
    }
}
