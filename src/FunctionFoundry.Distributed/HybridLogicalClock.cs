using System.Globalization;

namespace FunctionFoundry.Distributed;

/// <summary>
/// A hybrid logical clock timestamp combining physical time and a logical counter.
/// </summary>
/// <param name="PhysicalTimeMs">Physical time in UTC milliseconds since Unix epoch.</param>
/// <param name="LogicalCounter">Logical counter for events at the same physical millisecond.</param>
public readonly record struct HybridLogicalTimestamp(long PhysicalTimeMs, int LogicalCounter) : IComparable<HybridLogicalTimestamp>
{
    /// <inheritdoc />
    public int CompareTo(HybridLogicalTimestamp other)
    {
        int physical = PhysicalTimeMs.CompareTo(other.PhysicalTimeMs);
        return physical != 0 ? physical : LogicalCounter.CompareTo(other.LogicalCounter);
    }

    /// <summary>Less-than comparison.</summary>
    public static bool operator <(HybridLogicalTimestamp left, HybridLogicalTimestamp right)
        => left.CompareTo(right) < 0;

    /// <summary>Less-than-or-equal comparison.</summary>
    public static bool operator <=(HybridLogicalTimestamp left, HybridLogicalTimestamp right)
        => left.CompareTo(right) <= 0;

    /// <summary>Greater-than comparison.</summary>
    public static bool operator >(HybridLogicalTimestamp left, HybridLogicalTimestamp right)
        => left.CompareTo(right) > 0;

    /// <summary>Greater-than-or-equal comparison.</summary>
    public static bool operator >=(HybridLogicalTimestamp left, HybridLogicalTimestamp right)
        => left.CompareTo(right) >= 0;

    /// <summary>
    /// Serializes to deterministic <c>physical:logical</c> form.
    /// </summary>
    public string Serialize()
        => string.Create(CultureInfo.InvariantCulture, $"{PhysicalTimeMs}:{LogicalCounter}");

    /// <summary>
    /// Parses <see cref="Serialize"/> output.
    /// </summary>
    public static HybridLogicalTimestamp Deserialize(string serialized)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serialized);
        int colon = serialized.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0 || colon == serialized.Length - 1)
        {
            throw new FormatException($"Invalid hybrid logical timestamp '{serialized}'.");
        }

        if (!long.TryParse(serialized.AsSpan(0, colon), NumberStyles.Integer, CultureInfo.InvariantCulture, out long physical)
            || !int.TryParse(serialized.AsSpan(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int logical)
            || logical < 0)
        {
            throw new FormatException($"Invalid hybrid logical timestamp '{serialized}'.");
        }

        return new HybridLogicalTimestamp(physical, logical);
    }
}

/// <summary>
/// Options for <see cref="HybridLogicalClock"/>.
/// </summary>
public sealed class HybridLogicalClockOptions
{
    /// <summary>
    /// Gets or sets the maximum logical counter before the clock forces a physical-time advance. Defaults to 1_000_000.
    /// </summary>
    public int MaxLogicalCounter { get; set; } = 1_000_000;

    /// <summary>
    /// Validates option ranges.
    /// </summary>
    public void Validate()
    {
        if (MaxLogicalCounter <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxLogicalCounter), "MaxLogicalCounter must be positive.");
        }
    }
}

/// <summary>
/// Hybrid logical clock (HLC) for causal ordering across nodes with loosely synchronized wall clocks.
/// </summary>
/// <remarks>
/// <para>Follows the CockroachDB/Google Spanner-style HLC update rules for local events and receive events.</para>
/// <para>Thread safety: instance methods are synchronized.</para>
/// </remarks>
public sealed class HybridLogicalClock
{
    private readonly IClock _clock;
    private readonly HybridLogicalClockOptions _options;
    private readonly object _gate = new();
    private HybridLogicalTimestamp _latest;

    /// <summary>
    /// Initializes a new HLC.
    /// </summary>
    /// <param name="clock">Physical clock source.</param>
    /// <param name="options">Optional growth limits.</param>
    public HybridLogicalClock(IClock? clock = null, HybridLogicalClockOptions? options = null)
    {
        _clock = clock ?? new SystemClock();
        _options = options ?? new HybridLogicalClockOptions();
        _options.Validate();
        _latest = default;
    }

    /// <summary>
    /// Gets the latest issued timestamp.
    /// </summary>
    public HybridLogicalTimestamp Latest
    {
        get
        {
            lock (_gate)
            {
                return _latest;
            }
        }
    }

    /// <summary>
    /// Issues a timestamp for a local event.
    /// </summary>
    public HybridLogicalTimestamp Now()
    {
        lock (_gate)
        {
            long physical = _clock.UnixTimeMilliseconds;
            if (physical > _latest.PhysicalTimeMs)
            {
                _latest = new HybridLogicalTimestamp(physical, 0);
            }
            else
            {
                int nextLogical = _latest.LogicalCounter + 1;
                if (nextLogical > _options.MaxLogicalCounter)
                {
                    _latest = new HybridLogicalTimestamp(_latest.PhysicalTimeMs + 1, 0);
                }
                else
                {
                    _latest = new HybridLogicalTimestamp(_latest.PhysicalTimeMs, nextLogical);
                }
            }

            return _latest;
        }
    }

    /// <summary>
    /// Updates the clock on receipt of a remote timestamp and returns the receive event timestamp.
    /// </summary>
    /// <param name="remote">Remote event timestamp.</param>
    public HybridLogicalTimestamp Receive(HybridLogicalTimestamp remote)
    {
        if (remote.LogicalCounter < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(remote), "Logical counter cannot be negative.");
        }

        lock (_gate)
        {
            long physical = _clock.UnixTimeMilliseconds;
            long maxPhysical = Math.Max(physical, Math.Max(_latest.PhysicalTimeMs, remote.PhysicalTimeMs));
            int logical;
            if (maxPhysical == _latest.PhysicalTimeMs && maxPhysical == remote.PhysicalTimeMs)
            {
                logical = Math.Max(_latest.LogicalCounter, remote.LogicalCounter) + 1;
            }
            else if (maxPhysical == _latest.PhysicalTimeMs)
            {
                logical = _latest.LogicalCounter + 1;
            }
            else if (maxPhysical == remote.PhysicalTimeMs)
            {
                logical = remote.LogicalCounter + 1;
            }
            else
            {
                logical = 0;
            }

            if (logical > _options.MaxLogicalCounter)
            {
                maxPhysical++;
                logical = 0;
            }

            _latest = new HybridLogicalTimestamp(maxPhysical, logical);
            return _latest;
        }
    }
}
