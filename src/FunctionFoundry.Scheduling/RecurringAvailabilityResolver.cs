namespace FunctionFoundry.Scheduling;

/// <summary>
/// Policy applied when local clock times are ambiguous or invalid during DST transitions.
/// </summary>
public enum LocalTimeAmbiguityPolicy
{
    /// <summary>Reject ambiguous or invalid local instants.</summary>
    Reject,

    /// <summary>Choose the earlier UTC offset for ambiguous instants and shift forward across invalid gaps.</summary>
    PreferEarlierOffset,

    /// <summary>Choose the later UTC offset for ambiguous instants and shift forward across invalid gaps.</summary>
    PreferLaterOffset,
}

/// <summary>
/// A recurring weekly availability window expressed in local time.
/// </summary>
/// <param name="DayOfWeek">Day of week in the participant time zone.</param>
/// <param name="StartLocal">Inclusive local start time-of-day.</param>
/// <param name="EndLocal">Exclusive local end time-of-day.</param>
public sealed record RecurringLocalWindow(DayOfWeek DayOfWeek, TimeOnly StartLocal, TimeOnly EndLocal);

/// <summary>
/// A participant with a time zone, recurring windows, and exception intervals.
/// </summary>
/// <param name="Id">Participant identifier.</param>
/// <param name="TimeZone">Participant time zone.</param>
/// <param name="RecurringWindows">Weekly recurring local windows.</param>
/// <param name="Exceptions">Unavailable UTC intervals overriding recurring windows.</param>
public sealed record AvailabilityParticipant(
    string Id,
    TimeZoneInfo TimeZone,
    IReadOnlyList<RecurringLocalWindow> RecurringWindows,
    IReadOnlyList<TimeInterval> Exceptions);

/// <summary>
/// A ranked candidate availability slot.
/// </summary>
/// <param name="Start">Inclusive UTC start.</param>
/// <param name="End">Exclusive UTC end.</param>
/// <param name="ParticipantIds">Participants available throughout the slot.</param>
/// <param name="RankScore">Lower scores are preferred.</param>
public sealed record AvailabilityCandidate(
    DateTimeOffset Start,
    DateTimeOffset End,
    IReadOnlyList<string> ParticipantIds,
    int RankScore);

/// <summary>
/// Options for recurring availability resolution.
/// </summary>
public sealed class RecurringAvailabilityResolverOptions
{
    /// <summary>
    /// Gets or sets the policy for ambiguous or invalid local times. Defaults to <see cref="LocalTimeAmbiguityPolicy.PreferEarlierOffset"/>.
    /// </summary>
    public LocalTimeAmbiguityPolicy AmbiguityPolicy { get; set; } = LocalTimeAmbiguityPolicy.PreferEarlierOffset;

    /// <summary>
    /// Gets or sets the search horizon in days from <c>rangeStart</c>. Defaults to 14.
    /// </summary>
    public int SearchHorizonDays { get; set; } = 14;

    /// <summary>
    /// Gets or sets the maximum number of ranked candidates returned. Defaults to 20.
    /// </summary>
    public int MaxCandidates { get; set; } = 20;

    /// <summary>
    /// Validates option ranges.
    /// </summary>
    public void Validate()
    {
        if (SearchHorizonDays <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(SearchHorizonDays), "SearchHorizonDays must be positive.");
        }

        if (MaxCandidates <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxCandidates), "MaxCandidates must be positive.");
        }
    }
}

/// <summary>
/// Resolves overlapping recurring availability across multiple participants and time zones.
/// </summary>
/// <remarks>
/// <para>Recurring windows are expanded in local time, converted to UTC with DST policies, and intersected across participants.</para>
/// <para>Exceptions remove availability regardless of recurring windows.</para>
/// </remarks>
public sealed class RecurringAvailabilityResolver
{
    private readonly RecurringAvailabilityResolverOptions _options;

    /// <summary>
    /// Initializes a new resolver.
    /// </summary>
    /// <param name="options">Optional resolver options.</param>
    public RecurringAvailabilityResolver(RecurringAvailabilityResolverOptions? options = null)
    {
        _options = options ?? new RecurringAvailabilityResolverOptions();
        _options.Validate();
    }

    /// <summary>
    /// Finds ranked candidate slots where all participants are available for at least <paramref name="minimumDuration"/>.
    /// </summary>
    /// <param name="participants">Participants with recurring windows and exceptions.</param>
    /// <param name="rangeStart">UTC search start.</param>
    /// <param name="minimumDuration">Required contiguous availability duration.</param>
    /// <returns>Ranked candidates in deterministic order.</returns>
    public IReadOnlyList<AvailabilityCandidate> Resolve(
        IReadOnlyList<AvailabilityParticipant> participants,
        DateTimeOffset rangeStart,
        TimeSpan minimumDuration)
    {
        ArgumentNullException.ThrowIfNull(participants);
        if (participants.Count == 0)
        {
            throw new ArgumentException("At least one participant is required.", nameof(participants));
        }

        if (minimumDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumDuration), "Minimum duration must be positive.");
        }

        DateTimeOffset rangeEnd = rangeStart.AddDays(_options.SearchHorizonDays);
        List<IReadOnlyList<TimeInterval>> perParticipant = new(participants.Count);
        foreach (AvailabilityParticipant participant in participants)
        {
            perParticipant.Add(ExpandParticipant(participant, rangeStart, rangeEnd));
        }

        IReadOnlyList<TimeInterval> overlap = perParticipant[0];
        for (int i = 1; i < perParticipant.Count; i++)
        {
            overlap = IntervalSetAlgebra.Intersection(overlap, perParticipant[i]);
        }

        List<AvailabilityCandidate> candidates = new();
        string[] participantIds = participants.Select(static p => p.Id).OrderBy(static id => id, StringComparer.Ordinal).ToArray();
        foreach (TimeInterval interval in overlap)
        {
            TimeSpan duration = interval.End - interval.Start;
            if (duration < minimumDuration)
            {
                continue;
            }

            DateTimeOffset cursor = interval.Start;
            while (cursor + minimumDuration <= interval.End)
            {
                DateTimeOffset candidateEnd = cursor + minimumDuration;
                candidates.Add(new AvailabilityCandidate(cursor, candidateEnd, participantIds, RankScore(cursor, participantIds.Length)));
                cursor = candidateEnd;
            }
        }

        return candidates
            .OrderBy(static c => c.RankScore)
            .ThenBy(static c => c.Start)
            .ThenBy(static c => c.End)
            .Take(_options.MaxCandidates)
            .ToArray();
    }

    private IReadOnlyList<TimeInterval> ExpandParticipant(AvailabilityParticipant participant, DateTimeOffset rangeStart, DateTimeOffset rangeEnd)
    {
        List<TimeInterval> generated = new();
        DateTimeOffset cursor = rangeStart;
        while (cursor < rangeEnd)
        {
            DateTime localDate = TimeZoneInfo.ConvertTime(cursor, participant.TimeZone).Date;
            foreach (RecurringLocalWindow window in participant.RecurringWindows)
            {
                if (localDate.DayOfWeek != window.DayOfWeek)
                {
                    continue;
                }

                if (!TryResolveLocalInstant(localDate, window.StartLocal, participant.TimeZone, out DateTimeOffset startUtc))
                {
                    continue;
                }

                if (!TryResolveLocalInstant(localDate, window.EndLocal, participant.TimeZone, out DateTimeOffset endUtc))
                {
                    continue;
                }

                if (endUtc <= startUtc)
                {
                    continue;
                }

                TimeInterval interval = new(startUtc, endUtc, IntervalEndpoint.Closed, IntervalEndpoint.Open);
                if (!interval.IsEmpty && interval.End > rangeStart && interval.Start < rangeEnd)
                {
                    generated.Add(interval);
                }
            }

            cursor = cursor.AddDays(1);
        }

        IReadOnlyList<TimeInterval> normalized = IntervalSetAlgebra.Normalize(generated);
        return IntervalSetAlgebra.Difference(normalized, participant.Exceptions);
    }

    private bool TryResolveLocalInstant(DateTime localDate, TimeOnly localTime, TimeZoneInfo zone, out DateTimeOffset utc)
    {
        DateTime unspecified = DateTime.SpecifyKind(localDate.Add(localTime.ToTimeSpan()), DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(unspecified))
        {
            if (_options.AmbiguityPolicy == LocalTimeAmbiguityPolicy.Reject)
            {
                utc = default;
                return false;
            }

            DateTime adjusted = unspecified.AddHours(1);
            utc = new DateTimeOffset(adjusted, zone.GetUtcOffset(adjusted));
            return true;
        }

        if (zone.IsAmbiguousTime(unspecified))
        {
            if (_options.AmbiguityPolicy == LocalTimeAmbiguityPolicy.Reject)
            {
                utc = default;
                return false;
            }

            TimeSpan[] offsets = zone.GetAmbiguousTimeOffsets(unspecified);
            TimeSpan chosen = _options.AmbiguityPolicy == LocalTimeAmbiguityPolicy.PreferEarlierOffset
                ? offsets.Min()
                : offsets.Max();
            utc = new DateTimeOffset(unspecified, chosen);
            return true;
        }

        utc = new DateTimeOffset(unspecified, zone.GetUtcOffset(unspecified));
        return true;
    }

    private static int RankScore(DateTimeOffset start, int participantCount)
        => (int)(start.DayOfWeek switch
        {
            DayOfWeek.Monday => 0,
            DayOfWeek.Tuesday => 1,
            DayOfWeek.Wednesday => 2,
            DayOfWeek.Thursday => 3,
            DayOfWeek.Friday => 4,
            _ => 10,
        }) + (participantCount * 100);
}
