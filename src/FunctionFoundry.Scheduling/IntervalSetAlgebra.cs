namespace FunctionFoundry.Scheduling;

/// <summary>
/// Endpoint inclusivity for an interval bound.
/// </summary>
public enum IntervalEndpoint
{
    /// <summary>The endpoint value is excluded.</summary>
    Open,

    /// <summary>The endpoint value is included.</summary>
    Closed,
}

/// <summary>
/// A half-bounded or fully bounded time interval with explicit endpoint inclusivity.
/// </summary>
/// <param name="Start">Interval start instant in UTC.</param>
/// <param name="End">Interval end instant in UTC. Must be after <paramref name="Start"/> for non-empty intervals.</param>
/// <param name="StartEndpoint">Inclusivity of <paramref name="Start"/>.</param>
/// <param name="EndEndpoint">Inclusivity of <paramref name="End"/>.</param>
public readonly record struct TimeInterval(
    DateTimeOffset Start,
    DateTimeOffset End,
    IntervalEndpoint StartEndpoint = IntervalEndpoint.Closed,
    IntervalEndpoint EndEndpoint = IntervalEndpoint.Closed)
{
    /// <summary>
    /// Gets a value indicating whether the interval contains any instants.
    /// </summary>
    public bool IsEmpty =>
        End < Start ||
        (End == Start && (StartEndpoint == IntervalEndpoint.Open || EndEndpoint == IntervalEndpoint.Open));
}

/// <summary>
/// Set algebra over normalized sorted intervals.
/// </summary>
/// <remarks>
/// <para>All operations return normalized intervals sorted by start time with deterministic tie-breaking on end time and endpoint inclusivity.</para>
/// <para>Inputs may be large; implementations operate in linear time relative to input size after normalization.</para>
/// </remarks>
public static class IntervalSetAlgebra
{
    /// <summary>
    /// Normalizes overlapping and adjacent intervals into a minimal sorted set.
    /// </summary>
    /// <param name="intervals">Input intervals.</param>
    /// <returns>Normalized non-overlapping intervals.</returns>
    public static IReadOnlyList<TimeInterval> Normalize(IEnumerable<TimeInterval> intervals)
    {
        ArgumentNullException.ThrowIfNull(intervals);
        List<TimeInterval> sorted = intervals
            .Where(static i => !i.IsEmpty)
            .OrderBy(static i => i.Start)
            .ThenBy(static i => i.End)
            .ThenBy(static i => i.StartEndpoint)
            .ThenBy(static i => i.EndEndpoint)
            .ToList();

        if (sorted.Count == 0)
        {
            return Array.Empty<TimeInterval>();
        }

        List<TimeInterval> merged = new(sorted.Count);
        TimeInterval current = sorted[0];
        for (int i = 1; i < sorted.Count; i++)
        {
            TimeInterval next = sorted[i];
            if (OverlapsOrTouches(current, next))
            {
                current = MergeAdjacent(current, next);
            }
            else
            {
                merged.Add(current);
                current = next;
            }
        }

        merged.Add(current);
        return merged;
    }

    /// <summary>
    /// Computes the union of two interval sets.
    /// </summary>
    public static IReadOnlyList<TimeInterval> Union(IEnumerable<TimeInterval> left, IEnumerable<TimeInterval> right)
        => Normalize(left.Concat(right));

    /// <summary>
    /// Computes the intersection of two normalized or arbitrary interval sets.
    /// </summary>
    public static IReadOnlyList<TimeInterval> Intersection(IEnumerable<TimeInterval> left, IEnumerable<TimeInterval> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        IReadOnlyList<TimeInterval> a = Normalize(left);
        IReadOnlyList<TimeInterval> b = Normalize(right);
        List<TimeInterval> result = new();
        int i = 0;
        int j = 0;
        while (i < a.Count && j < b.Count)
        {
            TimeInterval? overlap = IntersectPair(a[i], b[j]);
            if (overlap is TimeInterval value && !value.IsEmpty)
            {
                result.Add(value);
            }

            if (ComparePoints(a[i].End, a[i].EndEndpoint, b[j].End, b[j].EndEndpoint) < 0)
            {
                i++;
            }
            else
            {
                j++;
            }
        }

        return Normalize(result);
    }

    /// <summary>
    /// Computes <paramref name="left"/> minus <paramref name="right"/>.
    /// </summary>
    public static IReadOnlyList<TimeInterval> Difference(IEnumerable<TimeInterval> left, IEnumerable<TimeInterval> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        IReadOnlyList<TimeInterval> minuend = Normalize(left);
        IReadOnlyList<TimeInterval> subtrahend = Normalize(right);
        List<TimeInterval> result = new(minuend);
        foreach (TimeInterval subtract in subtrahend)
        {
            List<TimeInterval> next = new();
            foreach (TimeInterval interval in result)
            {
                next.AddRange(SubtractSingle(interval, subtract));
            }

            result = next;
        }

        return Normalize(result);
    }

    /// <summary>
    /// Computes the complement of <paramref name="intervals"/> within <paramref name="universe"/>.
    /// </summary>
    public static IReadOnlyList<TimeInterval> Complement(IEnumerable<TimeInterval> intervals, TimeInterval universe)
    {
        ArgumentNullException.ThrowIfNull(intervals);
        if (universe.IsEmpty)
        {
            throw new ArgumentException("Universe interval must not be empty.", nameof(universe));
        }

        return Difference([universe], intervals);
    }

    private static bool OverlapsOrTouches(TimeInterval left, TimeInterval right)
        => ComparePoints(left.End, left.EndEndpoint, right.Start, right.StartEndpoint) >= 0;

    private static TimeInterval MergeAdjacent(TimeInterval left, TimeInterval right)
    {
        bool takeRightEnd = ComparePoints(right.End, right.EndEndpoint, left.End, left.EndEndpoint) > 0;
        return new TimeInterval(
            left.Start,
            takeRightEnd ? right.End : left.End,
            left.StartEndpoint,
            takeRightEnd ? right.EndEndpoint : left.EndEndpoint);
    }

    private static TimeInterval? IntersectPair(TimeInterval left, TimeInterval right)
    {
        DateTimeOffset start = ComparePoints(left.Start, left.StartEndpoint, right.Start, right.StartEndpoint) >= 0
            ? left.Start
            : right.Start;
        IntervalEndpoint startEndpoint = ComparePoints(left.Start, left.StartEndpoint, right.Start, right.StartEndpoint) >= 0
            ? left.StartEndpoint
            : right.StartEndpoint;

        DateTimeOffset end = ComparePoints(left.End, left.EndEndpoint, right.End, right.EndEndpoint) <= 0
            ? left.End
            : right.End;
        IntervalEndpoint endEndpoint = ComparePoints(left.End, left.EndEndpoint, right.End, right.EndEndpoint) <= 0
            ? left.EndEndpoint
            : right.EndEndpoint;

        TimeInterval candidate = new(start, end, startEndpoint, endEndpoint);
        return candidate.IsEmpty ? null : candidate;
    }

    private static IEnumerable<TimeInterval> SubtractSingle(TimeInterval left, TimeInterval right)
    {
        TimeInterval? overlap = IntersectPair(left, right);
        if (overlap is null || overlap.Value.IsEmpty)
        {
            yield return left;
            yield break;
        }

        TimeInterval intersection = overlap.Value;
        if (ComparePoints(left.Start, left.StartEndpoint, intersection.Start, intersection.StartEndpoint) < 0)
        {
            yield return new TimeInterval(left.Start, intersection.Start, left.StartEndpoint, Flip(intersection.StartEndpoint));
        }

        if (ComparePoints(left.End, left.EndEndpoint, intersection.End, intersection.EndEndpoint) > 0)
        {
            yield return new TimeInterval(intersection.End, left.End, Flip(intersection.EndEndpoint), left.EndEndpoint);
        }
    }

    private static IntervalEndpoint Flip(IntervalEndpoint endpoint)
        => endpoint == IntervalEndpoint.Closed ? IntervalEndpoint.Open : IntervalEndpoint.Closed;

    private static int ComparePoints(DateTimeOffset left, IntervalEndpoint leftEndpoint, DateTimeOffset right, IntervalEndpoint rightEndpoint)
    {
        int cmp = left.CompareTo(right);
        if (cmp != 0)
        {
            return cmp;
        }

        if (leftEndpoint == IntervalEndpoint.Closed && rightEndpoint == IntervalEndpoint.Open)
        {
            return 1;
        }

        if (leftEndpoint == IntervalEndpoint.Open && rightEndpoint == IntervalEndpoint.Closed)
        {
            return -1;
        }

        return 0;
    }
}
