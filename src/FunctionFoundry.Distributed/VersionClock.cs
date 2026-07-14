using System.Text;

namespace FunctionFoundry.Distributed;

/// <summary>
/// Comparison relationship between two version clocks.
/// </summary>
public enum VersionClockRelation
{
    /// <summary>The left clock strictly happens-before the right clock.</summary>
    HappensBefore,

    /// <summary>The left clock strictly happens-after the right clock.</summary>
    HappensAfter,

    /// <summary>Neither clock dominates the other.</summary>
    Concurrent,

    /// <summary>Both clocks represent the same causal history.</summary>
    Equal,
}

/// <summary>
/// Options controlling version clock growth and compaction behavior.
/// </summary>
public sealed class VersionClockOptions
{
    /// <summary>
    /// Gets or sets the maximum number of node entries retained before compaction is required. Defaults to 256.
    /// </summary>
    public int MaxNodeEntries { get; set; } = 256;

    /// <summary>
    /// Validates option ranges.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <see cref="MaxNodeEntries"/> is not positive.</exception>
    public void Validate()
    {
        if (MaxNodeEntries <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxNodeEntries), "MaxNodeEntries must be positive.");
        }
    }
}

/// <summary>
/// A vector clock tracking per-node logical counters.
/// </summary>
/// <remarks>
/// <para>Compaction removes the lowest counters when growth limits are exceeded. Compaction can erase fine-grained causal detail for removed nodes and must be applied only when the host protocol tolerates that trade-off.</para>
/// <para>Serialization orders node ids ordinally for deterministic wire forms.</para>
/// </remarks>
public sealed class VectorClock
{
    private readonly VersionClockOptions _options;
    private readonly SortedDictionary<string, ulong> _entries;

    /// <summary>
    /// Initializes a new empty vector clock.
    /// </summary>
    /// <param name="options">Optional growth options.</param>
    public VectorClock(VersionClockOptions? options = null)
    {
        _options = options ?? new VersionClockOptions();
        _options.Validate();
        _entries = new SortedDictionary<string, ulong>(StringComparer.Ordinal);
    }

    /// <summary>
    /// Initializes a vector clock from existing entries.
    /// </summary>
    /// <param name="entries">Node counters copied into the clock.</param>
    /// <param name="options">Optional growth options.</param>
    public VectorClock(IEnumerable<KeyValuePair<string, ulong>> entries, VersionClockOptions? options = null)
        : this(options)
    {
        ArgumentNullException.ThrowIfNull(entries);
        foreach (KeyValuePair<string, ulong> entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Key))
            {
                throw new ArgumentException("Node ids must not be null or empty.", nameof(entries));
            }

            _entries[entry.Key] = entry.Value;
        }
    }

    /// <summary>
    /// Gets a read-only view of node counters in deterministic node-id order.
    /// </summary>
    public IReadOnlyDictionary<string, ulong> Entries => _entries;

    /// <summary>
    /// Increments the counter for <paramref name="nodeId"/> and returns the new value.
    /// </summary>
    /// <param name="nodeId">Node identifier. Must not be null or empty.</param>
    /// <returns>Updated counter for the node.</returns>
    public ulong Increment(string nodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ulong next = _entries.TryGetValue(nodeId, out ulong current) ? current + 1 : 1;
        _entries[nodeId] = next;
        EnforceGrowthLimit();
        return next;
    }

    /// <summary>
    /// Merges another vector clock by taking the per-node maximum.
    /// </summary>
    /// <param name="other">Clock to merge. Must not be null.</param>
    /// <returns>This clock after merge.</returns>
    public VectorClock Merge(VectorClock other)
    {
        ArgumentNullException.ThrowIfNull(other);
        foreach (KeyValuePair<string, ulong> entry in other._entries)
        {
            _entries[entry.Key] = _entries.TryGetValue(entry.Key, out ulong current)
                ? Math.Max(current, entry.Value)
                : entry.Value;
        }

        EnforceGrowthLimit();
        return this;
    }

    /// <summary>
    /// Compares this clock with <paramref name="other"/>.
    /// </summary>
    /// <param name="other">Clock to compare against.</param>
    /// <returns>Causal relationship.</returns>
    public VersionClockRelation CompareTo(VectorClock other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return CompareEntries(_entries, other._entries);
    }

    /// <summary>
    /// Serializes the clock to a deterministic dotted textual form: <c>node=counter,node=counter</c>.
    /// </summary>
    /// <returns>Canonical serialized representation.</returns>
    public string Serialize()
    {
        StringBuilder builder = new();
        bool first = true;
        foreach (KeyValuePair<string, ulong> entry in _entries)
        {
            if (!first)
            {
                builder.Append(',');
            }

            builder.Append(entry.Key);
            builder.Append('=');
            builder.Append(entry.Value);
            first = false;
        }

        return builder.ToString();
    }

    /// <summary>
    /// Deserializes a clock from <see cref="Serialize"/> output.
    /// </summary>
    /// <param name="serialized">Serialized clock text.</param>
    /// <param name="options">Optional growth options for the new clock.</param>
    /// <returns>Parsed vector clock.</returns>
    public static VectorClock Deserialize(string serialized, VersionClockOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(serialized);
        VectorClock clock = new(options);
        if (serialized.Length == 0)
        {
            return clock;
        }

        foreach (string segment in serialized.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int eq = segment.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0 || eq == segment.Length - 1)
            {
                throw new FormatException($"Invalid vector clock segment '{segment}'.");
            }

            string node = segment[..eq];
            if (!ulong.TryParse(segment[(eq + 1)..], out ulong value))
            {
                throw new FormatException($"Invalid counter in segment '{segment}'.");
            }

            clock._entries[node] = value;
        }

        return clock;
    }

    /// <summary>
    /// Compacts the clock by removing the lowest-counter nodes until within the growth limit.
    /// </summary>
    /// <returns>Number of removed node entries.</returns>
    public int Compact()
    {
        int removed = 0;
        while (_entries.Count > _options.MaxNodeEntries)
        {
            string? lowestNode = null;
            ulong lowestValue = ulong.MaxValue;
            foreach (KeyValuePair<string, ulong> entry in _entries)
            {
                if (entry.Value < lowestValue)
                {
                    lowestValue = entry.Value;
                    lowestNode = entry.Key;
                }
            }

            if (lowestNode is null)
            {
                break;
            }

            _entries.Remove(lowestNode);
            removed++;
        }

        return removed;
    }

    private void EnforceGrowthLimit()
    {
        if (_entries.Count > _options.MaxNodeEntries)
        {
            Compact();
        }
    }

    private static VersionClockRelation CompareEntries(
        IReadOnlyDictionary<string, ulong> left,
        IReadOnlyDictionary<string, ulong> right)
    {
        bool leftLess = false;
        bool rightLess = false;
        HashSet<string> nodes = new(left.Keys, StringComparer.Ordinal);
        nodes.UnionWith(right.Keys);

        foreach (string node in nodes)
        {
            left.TryGetValue(node, out ulong leftValue);
            right.TryGetValue(node, out ulong rightValue);
            if (leftValue < rightValue)
            {
                leftLess = true;
            }
            else if (leftValue > rightValue)
            {
                rightLess = true;
            }
        }

        if (leftLess && rightLess)
        {
            return VersionClockRelation.Concurrent;
        }

        if (leftLess)
        {
            return VersionClockRelation.HappensBefore;
        }

        if (rightLess)
        {
            return VersionClockRelation.HappensAfter;
        }

        return VersionClockRelation.Equal;
    }
}

/// <summary>
/// A dotted version vector entry combining a node counter and causal dots.
/// </summary>
/// <param name="NodeId">Node identifier.</param>
/// <param name="Counter">Current counter for the node.</param>
/// <param name="Dots">Historical counters excluded from the current counter.</param>
public sealed record DottedVersionEntry(string NodeId, ulong Counter, IReadOnlySet<ulong> Dots);

/// <summary>
/// A dotted version vector supporting concurrent updates with explicit dot tracking.
/// </summary>
/// <remarks>
/// <para>Dots record superseded counters and preserve concurrent-update semantics during merges.</para>
/// <para>Compaction collapses dot sets for the lowest-counter nodes when growth limits are exceeded, which can lose fine-grained concurrency metadata.</para>
/// </remarks>
public sealed class DottedVersionClock
{
    private readonly VersionClockOptions _options;
    private readonly SortedDictionary<string, (ulong Counter, SortedSet<ulong> Dots)> _entries;

    /// <summary>
    /// Initializes a new empty dotted version clock.
    /// </summary>
    /// <param name="options">Optional growth options.</param>
    public DottedVersionClock(VersionClockOptions? options = null)
    {
        _options = options ?? new VersionClockOptions();
        _options.Validate();
        _entries = new SortedDictionary<string, (ulong, SortedSet<ulong>)>(StringComparer.Ordinal);
    }

    /// <summary>
    /// Gets dotted entries in deterministic node-id order.
    /// </summary>
    public IReadOnlyList<DottedVersionEntry> Entries
    {
        get
        {
            List<DottedVersionEntry> result = new(_entries.Count);
            foreach (KeyValuePair<string, (ulong Counter, SortedSet<ulong> Dots)> entry in _entries)
            {
                result.Add(new DottedVersionEntry(entry.Key, entry.Value.Counter, entry.Value.Dots));
            }

            return result;
        }
    }

    /// <summary>
    /// Increments the counter for <paramref name="nodeId"/> and returns the new value.
    /// </summary>
    /// <param name="nodeId">Node identifier.</param>
    /// <returns>Updated counter.</returns>
    public ulong Increment(string nodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        if (!_entries.TryGetValue(nodeId, out (ulong Counter, SortedSet<ulong> Dots) current))
        {
            current = (0, new SortedSet<ulong>());
        }

        ulong next = current.Counter + 1;
        _entries[nodeId] = (next, current.Dots);
        EnforceGrowthLimit();
        return next;
    }

    /// <summary>
    /// Merges another dotted clock by unioning dots and taking maximum counters.
    /// </summary>
    /// <param name="other">Clock to merge.</param>
    /// <returns>This clock after merge.</returns>
    public DottedVersionClock Merge(DottedVersionClock other)
    {
        ArgumentNullException.ThrowIfNull(other);
        foreach (KeyValuePair<string, (ulong Counter, SortedSet<ulong> Dots)> entry in other._entries)
        {
            if (!_entries.TryGetValue(entry.Key, out (ulong Counter, SortedSet<ulong> Dots) current))
            {
                _entries[entry.Key] = (entry.Value.Counter, new SortedSet<ulong>(entry.Value.Dots));
                continue;
            }

            ulong mergedCounter = Math.Max(current.Counter, entry.Value.Counter);
            SortedSet<ulong> mergedDots = new(current.Dots);
            foreach (ulong dot in entry.Value.Dots)
            {
                mergedDots.Add(dot);
            }

            if (mergedCounter > current.Counter)
            {
                mergedDots.Add(current.Counter);
            }

            if (mergedCounter > entry.Value.Counter)
            {
                mergedDots.Add(entry.Value.Counter);
            }

            mergedDots.Remove(mergedCounter);
            _entries[entry.Key] = (mergedCounter, mergedDots);
        }

        EnforceGrowthLimit();
        return this;
    }

    /// <summary>
    /// Determines whether this clock dominates <paramref name="other"/> in the dotted-version sense.
    /// </summary>
    /// <param name="other">Clock to compare.</param>
    /// <returns><see langword="true"/> when all events in <paramref name="other"/> are causally precedented by this clock.</returns>
    public bool Dominates(DottedVersionClock other)
    {
        ArgumentNullException.ThrowIfNull(other);
        foreach (KeyValuePair<string, (ulong Counter, SortedSet<ulong> Dots)> entry in other._entries)
        {
            if (!_entries.TryGetValue(entry.Key, out (ulong Counter, SortedSet<ulong> Dots) current))
            {
                return false;
            }

            if (entry.Value.Counter > current.Counter)
            {
                return false;
            }

            if (entry.Value.Counter < current.Counter && !current.Dots.Contains(entry.Value.Counter))
            {
                return false;
            }

            foreach (ulong dot in entry.Value.Dots)
            {
                if (dot >= current.Counter || !current.Dots.Contains(dot))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Serializes the dotted clock to deterministic textual form.
    /// </summary>
    /// <returns>Canonical dotted representation.</returns>
    public string Serialize()
    {
        StringBuilder builder = new();
        bool firstNode = true;
        foreach (KeyValuePair<string, (ulong Counter, SortedSet<ulong> Dots)> entry in _entries)
        {
            if (!firstNode)
            {
                builder.Append(';');
            }

            builder.Append(entry.Key);
            builder.Append('=');
            builder.Append(entry.Value.Counter);
            if (entry.Value.Dots.Count > 0)
            {
                builder.Append(':');
                bool firstDot = true;
                foreach (ulong dot in entry.Value.Dots)
                {
                    if (!firstDot)
                    {
                        builder.Append('.');
                    }

                    builder.Append(dot);
                    firstDot = false;
                }
            }

            firstNode = false;
        }

        return builder.ToString();
    }

    /// <summary>
    /// Deserializes a dotted clock from <see cref="Serialize"/> output.
    /// </summary>
    /// <param name="serialized">Serialized dotted clock.</param>
    /// <param name="options">Optional growth options.</param>
    /// <returns>Parsed dotted clock.</returns>
    public static DottedVersionClock Deserialize(string serialized, VersionClockOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(serialized);
        DottedVersionClock clock = new(options);
        if (serialized.Length == 0)
        {
            return clock;
        }

        foreach (string segment in serialized.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int eq = segment.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0)
            {
                throw new FormatException($"Invalid dotted clock segment '{segment}'.");
            }

            string node = segment[..eq];
            string remainder = segment[(eq + 1)..];
            string counterPart;
            string? dotsPart = null;
            int colon = remainder.IndexOf(':', StringComparison.Ordinal);
            if (colon >= 0)
            {
                counterPart = remainder[..colon];
                dotsPart = remainder[(colon + 1)..];
            }
            else
            {
                counterPart = remainder;
            }

            if (!ulong.TryParse(counterPart, out ulong counter))
            {
                throw new FormatException($"Invalid dotted counter in segment '{segment}'.");
            }

            SortedSet<ulong> dots = new();
            if (dotsPart is not null && dotsPart.Length > 0)
            {
                foreach (string dotText in dotsPart.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (!ulong.TryParse(dotText, out ulong dot))
                    {
                        throw new FormatException($"Invalid dot '{dotText}'.");
                    }

                    dots.Add(dot);
                }
            }

            clock._entries[node] = (counter, dots);
        }

        return clock;
    }

    /// <summary>
    /// Compacts the dotted clock by removing lowest-counter node entries.
    /// </summary>
    /// <returns>Number of removed node entries.</returns>
    public int Compact()
    {
        int removed = 0;
        while (_entries.Count > _options.MaxNodeEntries)
        {
            string? lowestNode = null;
            ulong lowestCounter = ulong.MaxValue;
            foreach (KeyValuePair<string, (ulong Counter, SortedSet<ulong> Dots)> entry in _entries)
            {
                if (entry.Value.Counter < lowestCounter)
                {
                    lowestCounter = entry.Value.Counter;
                    lowestNode = entry.Key;
                }
            }

            if (lowestNode is null)
            {
                break;
            }

            _entries.Remove(lowestNode);
            removed++;
        }

        return removed;
    }

    private void EnforceGrowthLimit()
    {
        if (_entries.Count > _options.MaxNodeEntries)
        {
            Compact();
        }
    }
}
