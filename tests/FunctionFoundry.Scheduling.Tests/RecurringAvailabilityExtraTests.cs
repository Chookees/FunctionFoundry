namespace FunctionFoundry.Scheduling.Tests;

public sealed class RecurringAvailabilityExtraTests
{
    [Fact]
    public void Minimum_duration_filters_short_windows()
    {
        TimeZoneInfo utc = TimeZoneInfo.Utc;
        var resolver = new RecurringAvailabilityResolver();
        AvailabilityParticipant[] participants =
        [
            new("a", utc, [new RecurringLocalWindow(DayOfWeek.Wednesday, new TimeOnly(9, 0), new TimeOnly(10, 0))], []),
        ];
        DateTimeOffset rangeStart = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
        IReadOnlyList<AvailabilityCandidate> ranked = resolver.Resolve(participants, rangeStart, TimeSpan.FromHours(2));
        Assert.Empty(ranked);
    }

    [Fact]
    public void Null_participants_throw()
    {
        var resolver = new RecurringAvailabilityResolver();
        Assert.Throws<ArgumentNullException>(() => resolver.Resolve(null!, DateTimeOffset.UtcNow, TimeSpan.FromHours(1)));
    }

    [Fact]
    public void Non_positive_minimum_duration_throws()
    {
        var resolver = new RecurringAvailabilityResolver();
        AvailabilityParticipant[] participants =
        [
            new("a", TimeZoneInfo.Utc, [new RecurringLocalWindow(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(17, 0))], []),
        ];
        Assert.Throws<ArgumentOutOfRangeException>(() => resolver.Resolve(participants, DateTimeOffset.UtcNow, TimeSpan.Zero));
    }
}
