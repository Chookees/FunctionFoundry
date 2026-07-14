using FunctionFoundry.Scheduling;

DateTimeOffset start = new(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);
TimeInterval a = new(start.AddHours(9), start.AddHours(12));
TimeInterval b = new(start.AddHours(11), start.AddHours(15));
IReadOnlyList<TimeInterval> merged = IntervalSetAlgebra.Union([a], [b]);
Console.WriteLine($"Merged intervals: {merged.Count}");

var resolver = new RecurringAvailabilityResolver();
AvailabilityParticipant participant = new(
    "host",
    TimeZoneInfo.Utc,
    [new RecurringLocalWindow(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(12, 0))],
    []);
IReadOnlyList<AvailabilityCandidate> slots = resolver.Resolve([participant], start, TimeSpan.FromMinutes(30));
Console.WriteLine($"Availability candidates: {slots.Count}");

var calendar = new BusinessCalendar();
BusinessCalendarResult added = calendar.AddWorkingDuration(start.AddHours(10), TimeSpan.FromHours(4));
Console.WriteLine($"Business add result: {added.Instant:O} steps={added.Steps.Count}");

var scheduler = new CriticalPathScheduler();
CriticalPathScheduleResult schedule = scheduler.Schedule(
[
    new("design", 2, []),
    new("build", 5, ["design"]),
    new("ship", 1, ["build"]),
]);
Console.WriteLine($"Critical path: {string.Join(" -> ", schedule.CriticalPath)} duration={schedule.ProjectDuration}");
