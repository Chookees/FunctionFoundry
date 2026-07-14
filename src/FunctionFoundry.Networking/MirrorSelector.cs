namespace FunctionFoundry.Networking;

/// <summary>
/// Observed mirror measurements used for scoring.
/// </summary>
/// <param name="MirrorId">Stable mirror identifier.</param>
/// <param name="LatencyMilliseconds">Recent latency observation.</param>
/// <param name="ThroughputBytesPerSecond">Recent throughput observation.</param>
/// <param name="Succeeded">Whether the last attempt succeeded.</param>
/// <param name="IntegrityValid">Whether the last payload passed integrity checks.</param>
public sealed record MirrorObservation(
    string MirrorId,
    double LatencyMilliseconds,
    double ThroughputBytesPerSecond,
    bool Succeeded,
    bool IntegrityValid);

/// <summary>
/// Exportable evidence for a mirror selection decision.
/// </summary>
/// <param name="MirrorId">Selected or evaluated mirror identifier.</param>
/// <param name="Score">Final score in the range [0, 1].</param>
/// <param name="LatencyScore">Latency component.</param>
/// <param name="ThroughputScore">Throughput component.</param>
/// <param name="AvailabilityScore">Availability component.</param>
/// <param name="IntegrityScore">Integrity component.</param>
/// <param name="FailurePenalty">Decay-weighted failure penalty applied.</param>
public sealed record MirrorSelectionEvidence(
    string MirrorId,
    double Score,
    double LatencyScore,
    double ThroughputScore,
    double AvailabilityScore,
    double IntegrityScore,
    double FailurePenalty);

/// <summary>
/// Options for <see cref="MirrorSelector"/>.
/// </summary>
public sealed class MirrorSelectorOptions
{
    /// <summary>
    /// Gets or sets the latency weight. Defaults to 0.35.
    /// </summary>
    public double LatencyWeight { get; set; } = 0.35;

    /// <summary>
    /// Gets or sets the throughput weight. Defaults to 0.25.
    /// </summary>
    public double ThroughputWeight { get; set; } = 0.25;

    /// <summary>
    /// Gets or sets the availability weight. Defaults to 0.25.
    /// </summary>
    public double AvailabilityWeight { get; set; } = 0.25;

    /// <summary>
    /// Gets or sets the integrity weight. Defaults to 0.15.
    /// </summary>
    public double IntegrityWeight { get; set; } = 0.15;

    /// <summary>
    /// Gets or sets the failure decay factor per observation. Defaults to 0.85.
    /// </summary>
    public double FailureDecayFactor { get; set; } = 0.85;

    /// <summary>
    /// Gets or sets the minimum score delta required to switch mirrors. Defaults to 0.05.
    /// </summary>
    public double SwitchHysteresis { get; set; } = 0.05;

    /// <summary>
    /// Validates option ranges.
    /// </summary>
    public void Validate()
    {
        if (LatencyWeight < 0 || ThroughputWeight < 0 || AvailabilityWeight < 0 || IntegrityWeight < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(LatencyWeight), "Weights cannot be negative.");
        }

        double sum = LatencyWeight + ThroughputWeight + AvailabilityWeight + IntegrityWeight;
        if (Math.Abs(sum - 1.0) > 0.0001)
        {
            throw new ArgumentOutOfRangeException(nameof(LatencyWeight), "Weights must sum to 1.");
        }

        if (FailureDecayFactor is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(FailureDecayFactor), "FailureDecayFactor must be in (0, 1].");
        }

        if (SwitchHysteresis < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(SwitchHysteresis), "SwitchHysteresis cannot be negative.");
        }
    }
}

/// <summary>
/// Scores mirrors using latency, throughput, availability, and integrity signals.
/// </summary>
public sealed class MirrorSelector
{
    private readonly MirrorSelectorOptions _options;
    private readonly Dictionary<string, MirrorState> _states = new(StringComparer.Ordinal);
    private string? _currentSelection;

    /// <summary>
    /// Initializes a new selector.
    /// </summary>
    /// <param name="options">Selector options.</param>
    public MirrorSelector(MirrorSelectorOptions? options = null)
    {
        _options = options ?? new MirrorSelectorOptions();
        _options.Validate();
    }

    /// <summary>
    /// Records an observation for a mirror.
    /// </summary>
    /// <param name="observation">Observation values.</param>
    public void Record(MirrorObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentException.ThrowIfNullOrWhiteSpace(observation.MirrorId);
        if (!_states.TryGetValue(observation.MirrorId, out MirrorState? state))
        {
            state = new MirrorState();
            _states[observation.MirrorId] = state;
        }

        state.LatencyMs = observation.LatencyMilliseconds;
        state.ThroughputBps = observation.ThroughputBytesPerSecond;
        state.LastSucceeded = observation.Succeeded;
        state.LastIntegrityValid = observation.IntegrityValid;
        if (!observation.Succeeded)
        {
            state.FailurePenalty = Math.Min(1.0, state.FailurePenalty + (1.0 - state.FailurePenalty) * (1.0 - _options.FailureDecayFactor));
        }
        else
        {
            state.FailurePenalty *= _options.FailureDecayFactor;
        }
    }

    /// <summary>
    /// Selects the best mirror with hysteresis to avoid rapid oscillation.
    /// </summary>
    /// <param name="candidateMirrorIds">Candidate mirror identifiers in deterministic tie order.</param>
    /// <returns>Selected mirror id.</returns>
    public string Select(IReadOnlyList<string> candidateMirrorIds)
    {
        ArgumentNullException.ThrowIfNull(candidateMirrorIds);
        if (candidateMirrorIds.Count == 0)
        {
            throw new ArgumentException("At least one candidate mirror is required.", nameof(candidateMirrorIds));
        }

        List<(string Id, double Score)> ranked = candidateMirrorIds
            .Select(id => (Id: id, Score: ComputeScore(id)))
            .OrderByDescending(static t => t.Score)
            .ThenBy(static t => t.Id, StringComparer.Ordinal)
            .ToList();

        string best = ranked[0].Id;
        if (_currentSelection is null)
        {
            _currentSelection = best;
            return best;
        }

        double currentScore = ComputeScore(_currentSelection);
        double bestScore = ranked[0].Score;
        if (string.Equals(_currentSelection, best, StringComparison.Ordinal)
            || bestScore - currentScore < _options.SwitchHysteresis)
        {
            return _currentSelection;
        }

        _currentSelection = best;
        return best;
    }

    /// <summary>
    /// Exports evidence for a mirror.
    /// </summary>
    /// <param name="mirrorId">Mirror identifier.</param>
    public MirrorSelectionEvidence ExportEvidence(string mirrorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mirrorId);
        (double latency, double throughput, double availability, double integrity, double penalty) = GetComponents(mirrorId);
        double score = ComputeWeightedScore(latency, throughput, availability, integrity, penalty);
        return new MirrorSelectionEvidence(mirrorId, score, latency, throughput, availability, integrity, penalty);
    }

    private double ComputeScore(string mirrorId)
    {
        (double latency, double throughput, double availability, double integrity, double penalty) = GetComponents(mirrorId);
        return ComputeWeightedScore(latency, throughput, availability, integrity, penalty);
    }

    private (double Latency, double Throughput, double Availability, double Integrity, double Penalty) GetComponents(string mirrorId)
    {
        if (!_states.TryGetValue(mirrorId, out MirrorState? state))
        {
            return (0.5, 0.5, 0.5, 0.5, 0.0);
        }

        double latency = 1.0 / (1.0 + Math.Max(0.0, state.LatencyMs) / 100.0);
        double maxThroughput = _states.Values.Max(static s => s.ThroughputBps);
        double throughput = maxThroughput <= 0 ? 0.5 : state.ThroughputBps / maxThroughput;
        double availability = state.LastSucceeded ? 1.0 : 0.0;
        double integrity = state.LastIntegrityValid ? 1.0 : 0.0;
        return (latency, throughput, availability, integrity, state.FailurePenalty);
    }

    private double ComputeWeightedScore(double latency, double throughput, double availability, double integrity, double penalty)
    {
        double raw = (_options.LatencyWeight * latency)
            + (_options.ThroughputWeight * throughput)
            + (_options.AvailabilityWeight * availability)
            + (_options.IntegrityWeight * integrity);
        return Math.Max(0.0, raw - penalty);
    }

    private sealed class MirrorState
    {
        public double LatencyMs { get; set; } = 50;

        public double ThroughputBps { get; set; } = 1_000_000;

        public bool LastSucceeded { get; set; } = true;

        public bool LastIntegrityValid { get; set; } = true;

        public double FailurePenalty { get; set; }
    }
}
