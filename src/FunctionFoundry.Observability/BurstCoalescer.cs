namespace FunctionFoundry.Observability;

/// <summary>
/// A coalesced burst of repeated events sharing a fingerprint.
/// </summary>
/// <param name="Fingerprint">Stable event fingerprint.</param>
/// <param name="TotalCount">Total number of events observed in the window, including coalesced duplicates.</param>
/// <param name="DroppedCount">Number of events not retained as first, last, or samples.</param>
/// <param name="First">First event payload in the window.</param>
/// <param name="Last">Last event payload in the window.</param>
/// <param name="Samples">Representative sample payloads retained from the burst.</param>
/// <param name="WindowStartedAtUtc">UTC timestamp when the window opened.</param>
/// <param name="WindowEndedAtUtc">UTC timestamp when the window closed.</param>
public sealed record BurstSummary(
    string Fingerprint,
    long TotalCount,
    long DroppedCount,
    object? First,
    object? Last,
    IReadOnlyList<object?> Samples,
    DateTimeOffset WindowStartedAtUtc,
    DateTimeOffset WindowEndedAtUtc);

/// <summary>
/// Options controlling burst coalescing behavior.
/// </summary>
public sealed class BurstCoalescerOptions
{
    /// <summary>
    /// Gets or sets the coalescing time window. Defaults to 10 seconds.
    /// </summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets or sets the maximum number of distinct fingerprints tracked concurrently. Defaults to 1_000.
    /// </summary>
    public int MaxFingerprints { get; set; } = 1_000;

    /// <summary>
    /// Gets or sets the maximum representative samples retained per burst. Defaults to 3.
    /// </summary>
    public int MaxSamplesPerBurst { get; set; } = 3;

    /// <summary>
    /// Validates option ranges.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when options are invalid.</exception>
    public void Validate()
    {
        if (Window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(Window), "Window must be positive.");
        }

        if (MaxFingerprints <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxFingerprints), "MaxFingerprints must be positive.");
        }

        if (MaxSamplesPerBurst < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxSamplesPerBurst), "MaxSamplesPerBurst cannot be negative.");
        }
    }
}

/// <summary>
/// Coalesces bursts of repeated events in bounded time windows while preserving first, last, and representative samples.
/// </summary>
/// <remarks>
/// <para>Concurrent producers are supported. Cardinality is bounded by evicting the oldest active fingerprint windows.</para>
/// <para>Call <see cref="FlushExpired"/> or <see cref="FlushAll"/> to retrieve closed bursts.</para>
/// <para>Thread safety: all members are safe for concurrent use.</para>
/// </remarks>
public sealed class BurstCoalescer
{
    private readonly BurstCoalescerOptions _options;
    private readonly object _sync = new();
    private readonly Dictionary<string, BurstWindow> _windows = new(StringComparer.Ordinal);
    private readonly Queue<string> _evictionOrder = new();
    private readonly List<BurstSummary> _pendingSummaries = [];
    private readonly Func<DateTimeOffset> _clock;

    /// <summary>
    /// Initializes a new instance of the <see cref="BurstCoalescer"/> class.
    /// </summary>
    /// <param name="options">Coalescer options. When null, defaults are used.</param>
    /// <param name="clock">Clock used for window boundaries. When null, <see cref="DateTimeOffset.UtcNow"/> is used.</param>
    public BurstCoalescer(BurstCoalescerOptions? options = null, Func<DateTimeOffset>? clock = null)
    {
        _options = options ?? new BurstCoalescerOptions();
        _options.Validate();
        _clock = clock ?? (static () => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Records an event for <paramref name="fingerprint"/> with the supplied <paramref name="payload"/>.
    /// </summary>
    /// <param name="fingerprint">Stable event fingerprint. Must not be null or empty.</param>
    /// <param name="payload">Event payload to retain when sampled. May be null.</param>
    public void Record(string fingerprint, object? payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        DateTimeOffset now = _clock();
        lock (_sync)
        {
            if (!_windows.TryGetValue(fingerprint, out BurstWindow? window))
            {
                window = CreateWindow(fingerprint, now);
            }
            else if (now - window.StartedAt > _options.Window)
            {
                _pendingSummaries.Add(CreateSummary(fingerprint, window, window.StartedAt + _options.Window));
                ResetWindow(window, now);
            }

            window.TotalCount++;
            if (window.TotalCount == 1)
            {
                window.First = payload;
            }

            window.Last = payload;
            MaybeAddSample(window, payload);
        }
    }

    /// <summary>
    /// Retrieves and clears summaries queued by automatic window rotation during <see cref="Record"/>.
    /// </summary>
    /// <returns>Queued <see cref="BurstSummary"/> instances.</returns>
    public IReadOnlyList<BurstSummary> DrainPending()
    {
        lock (_sync)
        {
            if (_pendingSummaries.Count == 0)
            {
                return [];
            }

            var copy = _pendingSummaries.ToArray();
            _pendingSummaries.Clear();
            return copy;
        }
    }

    /// <summary>
    /// Flushes bursts whose windows have expired relative to <paramref name="now"/>.
    /// </summary>
    /// <param name="now">Current UTC timestamp. When null, the coalescer clock is used.</param>
    /// <returns>Closed <see cref="BurstSummary"/> instances.</returns>
    public IReadOnlyList<BurstSummary> FlushExpired(DateTimeOffset? now = null)
    {
        DateTimeOffset current = now ?? _clock();
        lock (_sync)
        {
            var summaries = new List<BurstSummary>(_pendingSummaries);
            _pendingSummaries.Clear();
            List<string> keys = _windows.Keys.ToList();
            foreach (string fingerprint in keys)
            {
                BurstWindow window = _windows[fingerprint];
                if (current - window.StartedAt <= _options.Window)
                {
                    continue;
                }

                summaries.Add(CreateSummary(fingerprint, window, current));
                _windows.Remove(fingerprint);
                RemoveFromEvictionQueue(fingerprint);
            }

            return summaries;
        }
    }

    /// <summary>
    /// Flushes all active bursts and clears tracked windows.
    /// </summary>
    /// <returns>Closed <see cref="BurstSummary"/> instances.</returns>
    public IReadOnlyList<BurstSummary> FlushAll()
    {
        DateTimeOffset current = _clock();
        lock (_sync)
        {
            var summaries = new List<BurstSummary>(_windows.Count);
            foreach (KeyValuePair<string, BurstWindow> pair in _windows)
            {
                summaries.Add(CreateSummary(pair.Key, pair.Value, current));
            }

            _windows.Clear();
            _evictionOrder.Clear();
            return summaries;
        }
    }

    /// <summary>
    /// Gets the number of active fingerprint windows.
    /// </summary>
    public int ActiveFingerprintCount
    {
        get
        {
            lock (_sync)
            {
                return _windows.Count;
            }
        }
    }

    private BurstWindow CreateWindow(string fingerprint, DateTimeOffset now)
    {
        if (_windows.Count >= _options.MaxFingerprints)
        {
            string evict = _evictionOrder.Dequeue();
            _windows.Remove(evict);
        }

        var window = new BurstWindow { StartedAt = now };
        _windows[fingerprint] = window;
        _evictionOrder.Enqueue(fingerprint);
        return window;
    }

    private static void ResetWindow(BurstWindow window, DateTimeOffset now)
    {
        window.StartedAt = now;
        window.TotalCount = 0;
        window.First = null;
        window.Last = null;
        window.Samples.Clear();
    }

    private void MaybeAddSample(BurstWindow window, object? payload)
    {
        if (_options.MaxSamplesPerBurst == 0)
        {
            return;
        }

        if (window.Samples.Count >= _options.MaxSamplesPerBurst)
        {
            return;
        }

        if (window.TotalCount == 1 || window.TotalCount % ComputeSampleStride(window.TotalCount) == 0)
        {
            window.Samples.Add(payload);
        }
    }

    private static int ComputeSampleStride(long totalCount)
    {
        if (totalCount <= 4)
        {
            return 1;
        }

        return (int)Math.Max(2, Math.Sqrt(totalCount));
    }

    private static BurstSummary CreateSummary(string fingerprint, BurstWindow window, DateTimeOffset endedAt)
    {
        long retained = ComputeRetainedRepresentations(window);
        long dropped = Math.Max(0, window.TotalCount - retained);
        return new(
            fingerprint,
            window.TotalCount,
            dropped,
            window.First,
            window.Last,
            window.Samples.ToArray(),
            window.StartedAt,
            endedAt);
    }

    private static long ComputeRetainedRepresentations(BurstWindow window)
    {
        if (window.TotalCount == 0)
        {
            return 0;
        }

        long retained = 1;
        if (window.TotalCount > 1)
        {
            retained++;
        }

        retained += window.Samples.Count;
        return Math.Min(window.TotalCount, retained);
    }

    private void RemoveFromEvictionQueue(string fingerprint)
    {
        if (_evictionOrder.Count == 0)
        {
            return;
        }

        int count = _evictionOrder.Count;
        for (int i = 0; i < count; i++)
        {
            string current = _evictionOrder.Dequeue();
            if (!string.Equals(current, fingerprint, StringComparison.Ordinal))
            {
                _evictionOrder.Enqueue(current);
            }
        }
    }

    private sealed class BurstWindow
    {
        internal DateTimeOffset StartedAt { get; set; }

        internal long TotalCount { get; set; }

        internal object? First { get; set; }

        internal object? Last { get; set; }

        internal List<object?> Samples { get; } = [];
    }
}
