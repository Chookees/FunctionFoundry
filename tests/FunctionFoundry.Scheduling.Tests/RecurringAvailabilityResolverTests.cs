namespace FunctionFoundry.Scheduling.Tests;

public sealed class RecurringAvailabilityResolverTests
{
    [Fact]
    public void Resolves_overlap_across_time_zones()
    {
        TimeZoneInfo utc = TimeZoneInfo.Utc;
        TimeZoneInfo eastern = TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "Eastern Standard Time" : "America/New_York");
        var resolver = new RecurringAvailabilityResolver();
        AvailabilityParticipant[] participants =
        [
            new("alice", utc, [new RecurringLocalWindow(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(17, 0))], []),
            new("bob", eastern, [new RecurringLocalWindow(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(17, 0))], []),
        ];
        DateTimeOffset rangeStart = new(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);
        IReadOnlyList<AvailabilityCandidate> candidates = resolver.Resolve(participants, rangeStart, TimeSpan.FromHours(1));
        Assert.NotEmpty(candidates);
        Assert.Contains("alice", candidates[0].ParticipantIds);
        Assert.Contains("bob", candidates[0].ParticipantIds);
    }

    [Fact]
    public void Exceptions_remove_availability()
    {
        DateTimeOffset start = new(2026, 1, 6, 14, 0, 0, TimeSpan.Zero);
        TimeInterval exception = new(start, start.AddHours(2));
        AvailabilityParticipant participant = new(
            "solo",
            TimeZoneInfo.Utc,
            [new RecurringLocalWindow(DayOfWeek.Tuesday, new TimeOnly(9, 0), new TimeOnly(17, 0))],
            [exception]);
        var resolver = new RecurringAvailabilityResolver();
        IReadOnlyList<AvailabilityCandidate> candidates = resolver.Resolve([participant], new DateTimeOffset(2026, 1, 6, 0, 0, 0, TimeSpan.Zero), TimeSpan.FromHours(1));
        Assert.DoesNotContain(candidates, c => c.Start == start);
    }
}
