namespace FunctionFoundry.Observability;

/// <summary>
/// A stable sampling decision for an event fingerprint.
/// </summary>
/// <param name="ShouldSample"><see langword="true"/> when the event should be retained.</param>
/// <param name="EffectiveRate">Effective sampling rate applied to this fingerprint.</param>
/// <param name="Reason">Human-readable explanation of the decision.</param>
public sealed record SamplingDecision(bool ShouldSample, double EffectiveRate, string Reason);

/// <summary>
/// Exportable statistics from an <see cref="AdaptiveSampler"/>.
/// </summary>
/// <param name="TotalDecisions">Total number of sampling decisions made.</param>
/// <param name="SampledCount">Number of events selected for retention.</param>
/// <param name="DroppedCount">Number of events dropped by sampling.</param>
/// <param name="TrackedFingerprintCount">Number of fingerprints currently tracked.</param>
/// <param name="CurrentGlobalRate">Current adapted global sampling rate.</param>
/// <param name="HighSeverityBypassCount">Number of events always retained due to severity bypass.</param>
public sealed record AdaptiveSamplerStats(
    long TotalDecisions,
    long SampledCount,
    long DroppedCount,
    int TrackedFingerprintCount,
    double CurrentGlobalRate,
    long HighSeverityBypassCount);

/// <summary>
/// Options controlling adaptive, fingerprint-stable sampling.
/// </summary>
public sealed class AdaptiveSamplerOptions
{
    /// <summary>
    /// Gets or sets the maximum number of distinct fingerprints tracked. Defaults to 10_000.
    /// </summary>
    public int MaxTrackedFingerprints { get; set; } = 10_000;

    /// <summary>
    /// Gets or sets the baseline sampling rate in the range [0, 1]. Defaults to 1.0.
    /// </summary>
    public double BaseSampleRate { get; set; } = 1.0;

    /// <summary>
    /// Gets or sets the minimum adapted sampling rate. Defaults to 0.01.
    /// </summary>
    public double MinSampleRate { get; set; } = 0.01;

    /// <summary>
    /// Gets or sets the severity at and above which events are always sampled. Defaults to <see cref="EventSeverity.Error"/>.
    /// </summary>
    public EventSeverity AlwaysSampleFromSeverity { get; set; } = EventSeverity.Error;

    /// <summary>
    /// Gets or sets the decision seed used for deterministic fingerprint sampling. Defaults to zero.
    /// </summary>
    public uint DecisionSeed { get; set; }

    /// <summary>
    /// Gets or sets the number of events observed before global rate adaptation begins. Defaults to 1_000.
    /// </summary>
    public long AdaptationStartVolume { get; set; } = 1_000;

    /// <summary>
    /// Validates option ranges.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when numeric options are out of range.</exception>
    public void Validate()
    {
        if (MaxTrackedFingerprints <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxTrackedFingerprints), "MaxTrackedFingerprints must be positive.");
        }

        if (BaseSampleRate is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(BaseSampleRate), "BaseSampleRate must be between 0 and 1.");
        }

        if (MinSampleRate is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MinSampleRate), "MinSampleRate must be between 0 and 1.");
        }

        if (MinSampleRate > BaseSampleRate)
        {
            throw new ArgumentOutOfRangeException(nameof(MinSampleRate), "MinSampleRate cannot exceed BaseSampleRate.");
        }

        if (AdaptationStartVolume < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(AdaptationStartVolume), "AdaptationStartVolume cannot be negative.");
        }
    }
}

/// <summary>
/// Adaptively samples events while preserving high-severity events and stable decisions per fingerprint.
/// </summary>
/// <remarks>
/// <para>Identical fingerprints receive stable pass/drop decisions for a given effective rate using deterministic hashing.</para>
/// <para>Global sampling rate decreases as total volume grows and as tracked fingerprint cardinality increases.</para>
/// <para>Thread safety: all members are safe for concurrent use.</para>
/// </remarks>
public sealed class AdaptiveSampler
{
    private readonly AdaptiveSamplerOptions _options;
    private readonly object _sync = new();
    private readonly Dictionary<string, FingerprintState> _fingerprints = new(StringComparer.Ordinal);
    private readonly Queue<string> _evictionOrder = new();

    private long _totalDecisions;
    private long _sampledCount;
    private long _droppedCount;
    private long _highSeverityBypassCount;
    private long _totalVolume;
    private double _currentGlobalRate;

    /// <summary>
    /// Initializes a new instance of the <see cref="AdaptiveSampler"/> class.
    /// </summary>
    /// <param name="options">Sampler options. When null, defaults are used.</param>
    public AdaptiveSampler(AdaptiveSamplerOptions? options = null)
    {
        _options = options ?? new AdaptiveSamplerOptions();
        _options.Validate();
        _currentGlobalRate = _options.BaseSampleRate;
    }

    /// <summary>
    /// Determines whether an event with <paramref name="fingerprint"/> and <paramref name="severity"/> should be sampled.
    /// </summary>
    /// <param name="fingerprint">Stable event fingerprint hash. Must not be null or empty.</param>
    /// <param name="severity">Event severity.</param>
    /// <returns>A <see cref="SamplingDecision"/> describing retention.</returns>
    public SamplingDecision Decide(string fingerprint, EventSeverity severity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        lock (_sync)
        {
            _totalDecisions++;
            _totalVolume++;
            AdaptGlobalRate();

            if (severity >= _options.AlwaysSampleFromSeverity)
            {
                _sampledCount++;
                _highSeverityBypassCount++;
                TrackFingerprint(fingerprint);
                return new SamplingDecision(true, 1.0, "severity-bypass");
            }

            double effectiveRate = ComputeEffectiveRate(fingerprint);
            bool shouldSample = IsDeterministicSample(fingerprint, effectiveRate);
            TrackFingerprint(fingerprint);
            if (shouldSample)
            {
                _sampledCount++;
            }
            else
            {
                _droppedCount++;
            }

            return new SamplingDecision(shouldSample, effectiveRate, shouldSample ? "deterministic-pass" : "deterministic-drop");
        }
    }

    /// <summary>
    /// Exports current sampler statistics.
    /// </summary>
    public AdaptiveSamplerStats Stats
    {
        get
        {
            lock (_sync)
            {
                return new AdaptiveSamplerStats(
                    _totalDecisions,
                    _sampledCount,
                    _droppedCount,
                    _fingerprints.Count,
                    _currentGlobalRate,
                    _highSeverityBypassCount);
            }
        }
    }

    private void AdaptGlobalRate()
    {
        if (_totalVolume < _options.AdaptationStartVolume)
        {
            _currentGlobalRate = _options.BaseSampleRate;
            return;
        }

        double volumeFactor = Math.Min(1.0, _options.AdaptationStartVolume / (double)_totalVolume);
        double cardinalityFactor = Math.Min(1.0, 256.0 / Math.Max(256, _fingerprints.Count));
        double adapted = _options.BaseSampleRate * volumeFactor * cardinalityFactor;
        _currentGlobalRate = Math.Max(_options.MinSampleRate, adapted);
    }

    private double ComputeEffectiveRate(string fingerprint)
    {
        if (!_fingerprints.TryGetValue(fingerprint, out FingerprintState? state))
        {
            return _currentGlobalRate;
        }

        double localFactor = Math.Min(1.0, 8.0 / Math.Max(8, state.ObservationCount));
        return Math.Max(_options.MinSampleRate, _currentGlobalRate * localFactor);
    }

    private bool IsDeterministicSample(string fingerprint, double effectiveRate)
    {
        if (effectiveRate >= 1.0)
        {
            return true;
        }

        if (effectiveRate <= 0.0)
        {
            return false;
        }

        uint hash = ObservabilityHashing.StableHash32(fingerprint, _options.DecisionSeed);
        double unit = hash / (double)uint.MaxValue;
        return unit < effectiveRate;
    }

    private void TrackFingerprint(string fingerprint)
    {
        if (_fingerprints.TryGetValue(fingerprint, out FingerprintState? existing))
        {
            existing.ObservationCount++;
            return;
        }

        if (_fingerprints.Count >= _options.MaxTrackedFingerprints)
        {
            string evict = _evictionOrder.Dequeue();
            _fingerprints.Remove(evict);
        }

        _fingerprints[fingerprint] = new FingerprintState();
        _evictionOrder.Enqueue(fingerprint);
    }

    private sealed class FingerprintState
    {
        internal int ObservationCount { get; set; } = 1;
    }
}
