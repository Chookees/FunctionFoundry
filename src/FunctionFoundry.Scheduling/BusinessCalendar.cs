namespace FunctionFoundry.Scheduling;

/// <summary>
/// A working-hours segment on a business day.
/// </summary>
/// <param name="Start">Inclusive segment start time-of-day.</param>
/// <param name="End">Exclusive segment end time-of-day.</param>
public sealed record WorkingHoursSegment(TimeOnly Start, TimeOnly End);

/// <summary>
/// A holiday or closure interval supplied by the caller.
/// </summary>
/// <param name="Start">Inclusive closure start in UTC.</param>
/// <param name="End">Exclusive closure end in UTC.</param>
/// <param name="Reason">Optional explanation.</param>
public sealed record CalendarClosure(DateTimeOffset Start, DateTimeOffset End, string? Reason = null);

/// <summary>
/// Options defining a business calendar.
/// </summary>
public sealed class BusinessCalendarOptions
{
    /// <summary>
    /// Gets or sets working days. Defaults to Monday through Friday.
    /// </summary>
    public IReadOnlySet<DayOfWeek> WorkingDays { get; set; } =
        new HashSet<DayOfWeek>([DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday]);

    /// <summary>
    /// Gets or sets split working-hour segments for each working day.
    /// </summary>
    public IReadOnlyList<WorkingHoursSegment> WorkingSegments { get; set; } =
        [new WorkingHoursSegment(new TimeOnly(9, 0), new TimeOnly(12, 0)), new WorkingHoursSegment(new TimeOnly(13, 0), new TimeOnly(17, 0))];

    /// <summary>
    /// Gets or sets caller-supplied closures and holidays.
    /// </summary>
    public IReadOnlyList<CalendarClosure> Closures { get; set; } = Array.Empty<CalendarClosure>();

    /// <summary>
    /// Gets or sets the time zone used to interpret working days and segments. Defaults to UTC.
    /// </summary>
    public TimeZoneInfo TimeZone { get; set; } = TimeZoneInfo.Utc;

    /// <summary>
    /// Validates calendar options.
    /// </summary>
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(WorkingDays);
        ArgumentNullException.ThrowIfNull(WorkingSegments);
        ArgumentNullException.ThrowIfNull(Closures);
        ArgumentNullException.ThrowIfNull(TimeZone);
        if (WorkingDays.Count == 0)
        {
            throw new ArgumentException("At least one working day is required.", nameof(WorkingDays));
        }

        if (WorkingSegments.Count == 0)
        {
            throw new ArgumentException("At least one working segment is required.", nameof(WorkingSegments));
        }

        foreach (WorkingHoursSegment segment in WorkingSegments)
        {
            if (segment.End <= segment.Start)
            {
                throw new ArgumentException("Working segments must have End after Start.", nameof(WorkingSegments));
            }
        }
    }
}

/// <summary>
/// A step recorded while performing business-time arithmetic.
/// </summary>
/// <param name="Description">Human-readable explanation.</param>
/// <param name="Instant">UTC instant after applying the step.</param>
public sealed record BusinessCalendarStep(string Description, DateTimeOffset Instant);

/// <summary>
/// Result of business-time arithmetic with an explainable path.
/// </summary>
/// <param name="Instant">Resulting UTC instant.</param>
/// <param name="Steps">Ordered explanation steps.</param>
public sealed record BusinessCalendarResult(DateTimeOffset Instant, IReadOnlyList<BusinessCalendarStep> Steps);

/// <summary>
/// Business-day and working-hours calendar without a built-in holiday database.
/// </summary>
/// <remarks>
/// <para>Closures and holidays are supplied by callers through <see cref="BusinessCalendarOptions.Closures"/>.</para>
/// <para>All arithmetic is performed in the configured time zone and converted back to UTC in results.</para>
/// </remarks>
public sealed class BusinessCalendar
{
    private readonly BusinessCalendarOptions _options;

    /// <summary>
    /// Initializes a new business calendar.
    /// </summary>
    /// <param name="options">Calendar options.</param>
    public BusinessCalendar(BusinessCalendarOptions? options = null)
    {
        _options = options ?? new BusinessCalendarOptions();
        _options.Validate();
    }

    /// <summary>
    /// Adds working duration forward from <paramref name="start"/>.
    /// </summary>
    /// <param name="start">Starting UTC instant.</param>
    /// <param name="workingDuration">Working time to add.</param>
    /// <returns>Result with explanation path.</returns>
    public BusinessCalendarResult AddWorkingDuration(DateTimeOffset start, TimeSpan workingDuration)
    {
        if (workingDuration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(workingDuration), "Duration must be non-negative.");
        }

        List<BusinessCalendarStep> steps = new();
        DateTimeOffset cursor = start;
        TimeSpan remaining = workingDuration;
        steps.Add(new BusinessCalendarStep("Start", cursor));

        while (remaining > TimeSpan.Zero)
        {
            cursor = AlignToNextWorkingInstant(cursor, steps);
            if (IsClosed(cursor))
            {
                cursor = SkipClosure(cursor, steps);
                continue;
            }

            DateTimeOffset segmentEnd = GetCurrentSegmentEnd(cursor);
            TimeSpan available = segmentEnd - cursor;
            if (available >= remaining)
            {
                cursor += remaining;
                steps.Add(new BusinessCalendarStep($"Add remaining working duration {remaining}", cursor));
                remaining = TimeSpan.Zero;
            }
            else
            {
                cursor = segmentEnd;
                remaining -= available;
                steps.Add(new BusinessCalendarStep($"Consume segment ({available}), continue", cursor));
            }
        }

        return new BusinessCalendarResult(cursor, steps);
    }

    /// <summary>
    /// Subtracts working duration backward from <paramref name="end"/>.
    /// </summary>
    /// <param name="end">Ending UTC instant.</param>
    /// <param name="workingDuration">Working time to subtract.</param>
    /// <returns>Result with explanation path.</returns>
    public BusinessCalendarResult SubtractWorkingDuration(DateTimeOffset end, TimeSpan workingDuration)
    {
        if (workingDuration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(workingDuration), "Duration must be non-negative.");
        }

        List<BusinessCalendarStep> steps = new();
        DateTimeOffset cursor = end;
        TimeSpan remaining = workingDuration;
        steps.Add(new BusinessCalendarStep("Start", cursor));

        while (remaining > TimeSpan.Zero)
        {
            cursor = AlignToPreviousWorkingInstant(cursor, steps);
            if (IsClosed(cursor))
            {
                cursor = SkipClosureBackward(cursor, steps);
                continue;
            }

            DateTimeOffset segmentStart = GetCurrentSegmentStart(cursor);
            TimeSpan available = cursor - segmentStart;
            if (available >= remaining)
            {
                cursor -= remaining;
                steps.Add(new BusinessCalendarStep($"Subtract remaining working duration {remaining}", cursor));
                remaining = TimeSpan.Zero;
            }
            else
            {
                cursor = segmentStart;
                remaining -= available;
                steps.Add(new BusinessCalendarStep($"Consume prior segment ({available}), continue", cursor));
            }
        }

        return new BusinessCalendarResult(cursor, steps);
    }

    private DateTimeOffset AlignToNextWorkingInstant(DateTimeOffset instant, List<BusinessCalendarStep> steps)
    {
        DateTimeOffset cursor = instant;
        for (int guard = 0; guard < 366; guard++)
        {
            DateTime local = TimeZoneInfo.ConvertTime(cursor, _options.TimeZone).DateTime;
            if (!_options.WorkingDays.Contains(local.DayOfWeek))
            {
                DateTime nextDay = local.Date.AddDays(1);
                cursor = new DateTimeOffset(nextDay, _options.TimeZone.GetUtcOffset(nextDay));
                steps.Add(new BusinessCalendarStep($"Skip non-working day {local.DayOfWeek}", cursor));
                continue;
            }

            TimeOnly time = TimeOnly.FromDateTime(local);
            WorkingHoursSegment? segment = _options.WorkingSegments.FirstOrDefault(s => time < s.End);
            if (segment is null)
            {
                DateTime nextDay = local.Date.AddDays(1);
                cursor = new DateTimeOffset(nextDay, _options.TimeZone.GetUtcOffset(nextDay));
                steps.Add(new BusinessCalendarStep("After last segment, move to next day", cursor));
                continue;
            }

            if (time < segment.Start)
            {
                DateTime aligned = local.Date.Add(segment.Start.ToTimeSpan());
                cursor = new DateTimeOffset(aligned, _options.TimeZone.GetUtcOffset(aligned));
                steps.Add(new BusinessCalendarStep($"Align to segment start {segment.Start}", cursor));
            }

            return cursor;
        }

        throw new InvalidOperationException("Unable to align to a working instant within one year.");
    }

    private DateTimeOffset AlignToPreviousWorkingInstant(DateTimeOffset instant, List<BusinessCalendarStep> steps)
    {
        DateTimeOffset cursor = instant;
        for (int guard = 0; guard < 366; guard++)
        {
            DateTime local = TimeZoneInfo.ConvertTime(cursor, _options.TimeZone).DateTime;
            if (!_options.WorkingDays.Contains(local.DayOfWeek))
            {
                DateTime prevDay = local.Date.AddDays(-1);
                cursor = new DateTimeOffset(prevDay.Add(_options.WorkingSegments[^1].End.ToTimeSpan()), _options.TimeZone.GetUtcOffset(prevDay));
                steps.Add(new BusinessCalendarStep($"Skip non-working day {local.DayOfWeek}", cursor));
                continue;
            }

            TimeOnly time = TimeOnly.FromDateTime(local);
            WorkingHoursSegment? segment = _options.WorkingSegments.LastOrDefault(s => time > s.Start);
            if (segment is null)
            {
                DateTime prevDay = local.Date.AddDays(-1);
                cursor = new DateTimeOffset(prevDay.Add(_options.WorkingSegments[^1].End.ToTimeSpan()), _options.TimeZone.GetUtcOffset(prevDay));
                steps.Add(new BusinessCalendarStep("Before first segment, move to previous day", cursor));
                continue;
            }

            if (time > segment.End)
            {
                DateTime aligned = local.Date.Add(segment.End.ToTimeSpan());
                cursor = new DateTimeOffset(aligned, _options.TimeZone.GetUtcOffset(aligned));
                steps.Add(new BusinessCalendarStep($"Align to segment end {segment.End}", cursor));
            }

            return cursor;
        }

        throw new InvalidOperationException("Unable to align to a working instant within one year.");
    }

    private bool IsClosed(DateTimeOffset instant)
        => _options.Closures.Any(c => instant >= c.Start && instant < c.End);

    private DateTimeOffset SkipClosure(DateTimeOffset instant, List<BusinessCalendarStep> steps)
    {
        CalendarClosure closure = _options.Closures.First(c => instant >= c.Start && instant < c.End);
        steps.Add(new BusinessCalendarStep($"Skip closure '{closure.Reason ?? "closure"}'", closure.End));
        return closure.End;
    }

    private DateTimeOffset SkipClosureBackward(DateTimeOffset instant, List<BusinessCalendarStep> steps)
    {
        CalendarClosure closure = _options.Closures.First(c => instant > c.Start && instant <= c.End);
        steps.Add(new BusinessCalendarStep($"Skip closure backward '{closure.Reason ?? "closure"}'", closure.Start));
        return closure.Start;
    }

    private DateTimeOffset GetCurrentSegmentEnd(DateTimeOffset instant)
    {
        DateTime local = TimeZoneInfo.ConvertTime(instant, _options.TimeZone).DateTime;
        TimeOnly time = TimeOnly.FromDateTime(local);
        WorkingHoursSegment segment = _options.WorkingSegments.First(s => time >= s.Start && time < s.End);
        DateTime endLocal = local.Date.Add(segment.End.ToTimeSpan());
        return new DateTimeOffset(endLocal, _options.TimeZone.GetUtcOffset(endLocal));
    }

    private DateTimeOffset GetCurrentSegmentStart(DateTimeOffset instant)
    {
        DateTime local = TimeZoneInfo.ConvertTime(instant, _options.TimeZone).DateTime;
        TimeOnly time = TimeOnly.FromDateTime(local);
        WorkingHoursSegment segment = _options.WorkingSegments.First(s => time > s.Start && time <= s.End);
        DateTime startLocal = local.Date.Add(segment.Start.ToTimeSpan());
        return new DateTimeOffset(startLocal, _options.TimeZone.GetUtcOffset(startLocal));
    }
}
