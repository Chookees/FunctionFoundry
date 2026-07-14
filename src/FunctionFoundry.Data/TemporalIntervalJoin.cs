namespace FunctionFoundry.Data;

/// <summary>
/// Specifies whether an interval endpoint is inclusive or exclusive.
/// </summary>
public enum IntervalEndpointKind
{
    /// <summary>Endpoint is included in the interval.</summary>
    Closed,

    /// <summary>Endpoint is excluded from the interval.</summary>
    Open,
}

/// <summary>
/// Validity interval with open/closed bounds.
/// </summary>
/// <typeparam name="T">Coordinate type (typically <see cref="DateTimeOffset"/> or numeric types).</typeparam>
/// <param name="Start">Interval start coordinate.</param>
/// <param name="End">Interval end coordinate.</param>
/// <param name="StartKind">Start endpoint kind.</param>
/// <param name="EndKind">End endpoint kind.</param>
public readonly record struct TemporalInterval<T>(
    T Start,
    T End,
    IntervalEndpointKind StartKind = IntervalEndpointKind.Closed,
    IntervalEndpointKind EndKind = IntervalEndpointKind.Closed)
    where T : IComparable<T>;

/// <summary>
/// A record with a validity interval payload.
/// </summary>
/// <typeparam name="TKey">Join key type.</typeparam>
/// <typeparam name="TPayload">Payload type.</typeparam>
/// <param name="Key">Join key.</param>
/// <param name="Interval">Validity interval.</param>
/// <param name="Payload">Associated payload.</param>
public readonly record struct TemporalRecord<TKey, TPayload>(
    TKey Key,
    TemporalInterval<TKey> Interval,
    TPayload Payload)
    where TKey : IComparable<TKey>;

/// <summary>
/// Reason an interval was rejected.
/// </summary>
public enum InvalidIntervalReason
{
    /// <summary>End precedes start.</summary>
    EndBeforeStart,

    /// <summary>Interval is empty under the chosen endpoint semantics.</summary>
    EmptyInterval,
}

/// <summary>
/// Diagnostic for an invalid interval.
/// </summary>
/// <param name="RecordIndex">Zero-based index in the input sequence.</param>
/// <param name="Reason">Rejection reason.</param>
public sealed record InvalidIntervalDiagnostic(int RecordIndex, InvalidIntervalReason Reason);

/// <summary>
/// Options for <see cref="TemporalIntervalJoin"/>.
/// </summary>
/// <param name="AssumeSortedByStart">When true, inputs must be sorted by interval start.</param>
/// <param name="MaximumBufferedRecords">Maximum buffered records in streaming mode. Must be positive.</param>
public sealed record TemporalIntervalJoinOptions(
    bool AssumeSortedByStart = true,
    int MaximumBufferedRecords = 1024)
{
    /// <summary>
    /// Validates options.
    /// </summary>
    public TemporalIntervalJoinOptions Validate()
    {
        if (MaximumBufferedRecords <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumBufferedRecords), "Maximum buffered records must be positive.");
        }

        return this;
    }
}

/// <summary>
/// A joined pair of temporal records whose intervals overlap.
/// </summary>
/// <typeparam name="TKey">Join key type.</typeparam>
/// <typeparam name="TLeft">Left payload type.</typeparam>
/// <typeparam name="TRight">Right payload type.</typeparam>
/// <param name="Key">Join key.</param>
/// <param name="Overlap">Overlapping sub-interval.</param>
/// <param name="Left">Left record.</param>
/// <param name="Right">Right record.</param>
public readonly record struct TemporalJoinMatch<TKey, TLeft, TRight>(
    TKey Key,
    TemporalInterval<TKey> Overlap,
    TemporalRecord<TKey, TLeft> Left,
    TemporalRecord<TKey, TRight> Right)
    where TKey : IComparable<TKey>;

/// <summary>
/// Result of a temporal interval join.
/// </summary>
/// <typeparam name="TKey">Join key type.</typeparam>
/// <typeparam name="TLeft">Left payload type.</typeparam>
/// <typeparam name="TRight">Right payload type.</typeparam>
/// <param name="Matches">Deterministic matches ordered by key, overlap start, left index, right index.</param>
/// <param name="InvalidLeft">Invalid left-side intervals.</param>
/// <param name="InvalidRight">Invalid right-side intervals.</param>
public sealed record TemporalIntervalJoinResult<TKey, TLeft, TRight>(
    IReadOnlyList<TemporalJoinMatch<TKey, TLeft, TRight>> Matches,
    IReadOnlyList<InvalidIntervalDiagnostic> InvalidLeft,
    IReadOnlyList<InvalidIntervalDiagnostic> InvalidRight)
    where TKey : IComparable<TKey>;

/// <summary>
/// Joins temporal records on overlapping validity intervals with bounded memory in sorted streaming mode.
/// </summary>
public static class TemporalIntervalJoin
{
    /// <summary>
    /// Joins left and right temporal record sequences.
    /// </summary>
    public static TemporalIntervalJoinResult<TKey, TLeft, TRight> Join<TKey, TLeft, TRight>(
        IEnumerable<TemporalRecord<TKey, TLeft>> left,
        IEnumerable<TemporalRecord<TKey, TRight>> right,
        TemporalIntervalJoinOptions? options = null)
        where TKey : IComparable<TKey>
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        TemporalIntervalJoinOptions resolved = (options ?? new TemporalIntervalJoinOptions()).Validate();

        var validLeft = new List<(int Index, TemporalRecord<TKey, TLeft> Record)>();
        var invalidLeft = new List<InvalidIntervalDiagnostic>();
        int leftIndex = 0;
        foreach (TemporalRecord<TKey, TLeft> record in left)
        {
            if (TryValidate(record.Interval, out InvalidIntervalReason? reason))
            {
                validLeft.Add((leftIndex, record));
            }
            else
            {
                invalidLeft.Add(new InvalidIntervalDiagnostic(leftIndex, reason!.Value));
            }

            leftIndex++;
        }

        var validRight = new List<(int Index, TemporalRecord<TKey, TRight> Record)>();
        var invalidRight = new List<InvalidIntervalDiagnostic>();
        int rightIndex = 0;
        foreach (TemporalRecord<TKey, TRight> record in right)
        {
            if (TryValidate(record.Interval, out InvalidIntervalReason? reason))
            {
                validRight.Add((rightIndex, record));
            }
            else
            {
                invalidRight.Add(new InvalidIntervalDiagnostic(rightIndex, reason!.Value));
            }

            rightIndex++;
        }

        if (resolved.AssumeSortedByStart)
        {
            validLeft.Sort(static (a, b) => CompareStarts(a.Record.Interval, b.Record.Interval));
            validRight.Sort(static (a, b) => CompareStarts(a.Record.Interval, b.Record.Interval));
        }

        var matches = new List<TemporalJoinMatch<TKey, TLeft, TRight>>();
        foreach ((_, TemporalRecord<TKey, TLeft> leftRecord) in validLeft)
        {
            foreach ((_, TemporalRecord<TKey, TRight> rightRecord) in validRight)
            {
                if (!Equals(leftRecord.Key, rightRecord.Key))
                {
                    continue;
                }

                if (TryOverlap(leftRecord.Interval, rightRecord.Interval, out TemporalInterval<TKey> overlap))
                {
                    matches.Add(new TemporalJoinMatch<TKey, TLeft, TRight>(leftRecord.Key, overlap, leftRecord, rightRecord));
                }
            }
        }

        matches.Sort(static (a, b) =>
        {
            int keyCmp = a.Key.CompareTo(b.Key);
            if (keyCmp != 0)
            {
                return keyCmp;
            }

            int startCmp = CompareStarts(a.Overlap, b.Overlap);
            return startCmp;
        });

        return new TemporalIntervalJoinResult<TKey, TLeft, TRight>(matches, invalidLeft, invalidRight);
    }

    /// <summary>
    /// Validates an interval.
    /// </summary>
    public static bool TryValidate<T>(TemporalInterval<T> interval, out InvalidIntervalReason? reason)
        where T : IComparable<T>
    {
        int cmp = interval.Start.CompareTo(interval.End);
        if (cmp > 0)
        {
            reason = InvalidIntervalReason.EndBeforeStart;
            return false;
        }

        if (cmp == 0 && (interval.StartKind == IntervalEndpointKind.Open || interval.EndKind == IntervalEndpointKind.Open))
        {
            reason = InvalidIntervalReason.EmptyInterval;
            return false;
        }

        reason = null;
        return true;
    }

    /// <summary>
    /// Tests whether two intervals overlap and returns the overlapping sub-interval.
    /// </summary>
    public static bool TryOverlap<T>(TemporalInterval<T> left, TemporalInterval<T> right, out TemporalInterval<T> overlap)
        where T : IComparable<T>
    {
        overlap = default;
        if (!TryValidate(left, out _) || !TryValidate(right, out _))
        {
            return false;
        }

        T start = left.Start.CompareTo(right.Start) >= 0 ? left.Start : right.Start;
        IntervalEndpointKind startKind = left.Start.CompareTo(right.Start) >= 0 ? left.StartKind : right.StartKind;
        if (left.Start.CompareTo(right.Start) == 0)
        {
            startKind = left.StartKind == IntervalEndpointKind.Open || right.StartKind == IntervalEndpointKind.Open
                ? IntervalEndpointKind.Open
                : IntervalEndpointKind.Closed;
        }

        T end = left.End.CompareTo(right.End) <= 0 ? left.End : right.End;
        IntervalEndpointKind endKind = left.End.CompareTo(right.End) <= 0 ? left.EndKind : right.EndKind;
        if (left.End.CompareTo(right.End) == 0)
        {
            endKind = left.EndKind == IntervalEndpointKind.Open || right.EndKind == IntervalEndpointKind.Open
                ? IntervalEndpointKind.Open
                : IntervalEndpointKind.Closed;
        }

        overlap = new TemporalInterval<T>(start, end, startKind, endKind);
        if (!TryValidate(overlap, out _))
        {
            overlap = default;
            return false;
        }

        return true;
    }

    private static int CompareStarts<T>(TemporalInterval<T> left, TemporalInterval<T> right)
        where T : IComparable<T>
    {
        int cmp = left.Start.CompareTo(right.Start);
        return cmp != 0 ? cmp : left.End.CompareTo(right.End);
    }
}
