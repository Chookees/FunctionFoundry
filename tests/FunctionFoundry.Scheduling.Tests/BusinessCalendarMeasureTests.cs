using FunctionFoundry.Scheduling;

namespace FunctionFoundry.Scheduling.Tests;

public sealed class BusinessCalendarMeasureTests
{
    [Fact]
    public void MeasureWorkingDuration_counts_only_working_segments()
    {
        var calendar = new BusinessCalendar(new BusinessCalendarOptions
        {
            WorkingDays = new HashSet<DayOfWeek> { DayOfWeek.Monday },
            WorkingSegments = [new WorkingHoursSegment(new TimeOnly(9, 0), new TimeOnly(10, 0))],
            TimeZone = TimeZoneInfo.Utc,
        });

        // Monday 2026-08-03 is a Monday.
        DateTimeOffset start = new(2026, 8, 3, 8, 0, 0, TimeSpan.Zero);
        DateTimeOffset end = new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);
        TimeSpan measured = calendar.MeasureWorkingDuration(start, end);
        Assert.Equal(TimeSpan.FromHours(1), measured);
    }

    [Fact]
    public void MeasureWorkingDuration_rejects_inverted_range()
    {
        var calendar = new BusinessCalendar();
        DateTimeOffset start = new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset end = new(2026, 8, 3, 10, 0, 0, TimeSpan.Zero);
        Assert.Throws<ArgumentOutOfRangeException>(() => calendar.MeasureWorkingDuration(start, end));
    }
}
