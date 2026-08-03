using System.Collections.Concurrent;

namespace FunctionFoundry.Observability;

/// <summary>
/// Decision returned by <see cref="CardinalityLimiter.Observe"/>.
/// </summary>
public enum CardinalityDecisionKind
{
    /// <summary>Key was already tracked within the cardinality budget.</summary>
    AlreadySeen,

    /// <summary>Key was newly admitted within the cardinality budget.</summary>
    Allowed,

    /// <summary>Key exceeded the budget and was mapped to the overflow bucket.</summary>
    Overflow,
}

/// <summary>
/// Result of observing a dimension/key pair.
/// </summary>
/// <param name="Kind">Admission decision.</param>
/// <param name="EffectiveKey">Key to emit (may be the overflow label).</param>
public sealed record CardinalityDecision(CardinalityDecisionKind Kind, string EffectiveKey);

/// <summary>
/// Options for <see cref="CardinalityLimiter"/>.
/// </summary>
public sealed class CardinalityLimiterOptions
{
    /// <summary>
    /// Gets or sets the maximum distinct keys retained per dimension. Defaults to 1_024.
    /// </summary>
    public int MaxKeysPerDimension { get; set; } = 1_024;

    /// <summary>
    /// Gets or sets the overflow bucket label. Defaults to <c>__overflow__</c>.
    /// </summary>
    public string OverflowKey { get; set; } = "__overflow__";

    /// <summary>
    /// Validates options.
    /// </summary>
    public void Validate()
    {
        if (MaxKeysPerDimension <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxKeysPerDimension), "MaxKeysPerDimension must be positive.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(OverflowKey);
    }
}

/// <summary>
/// Bounds high-cardinality label/key sets per named dimension for telemetry pipelines.
/// </summary>
/// <remarks>
/// <para>Thread safety: instance methods are thread-safe.</para>
/// <para>Overflow keys collapse to a deterministic bucket label rather than being dropped silently.</para>
/// </remarks>
public sealed class CardinalityLimiter
{
    private readonly CardinalityLimiterOptions _options;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _dimensions =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new limiter.
    /// </summary>
    /// <param name="options">Optional limits.</param>
    public CardinalityLimiter(CardinalityLimiterOptions? options = null)
    {
        _options = options ?? new CardinalityLimiterOptions();
        _options.Validate();
    }

    /// <summary>
    /// Observes <paramref name="key"/> within <paramref name="dimension"/> and returns an admission decision.
    /// </summary>
    /// <param name="dimension">Dimension name (for example, a metric label key).</param>
    /// <param name="key">Observed value.</param>
    public CardinalityDecision Observe(string dimension, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dimension);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        ConcurrentDictionary<string, byte> keys = _dimensions.GetOrAdd(
            dimension,
            static _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));

        if (keys.ContainsKey(key))
        {
            return new CardinalityDecision(CardinalityDecisionKind.AlreadySeen, key);
        }

        if (keys.Count >= _options.MaxKeysPerDimension)
        {
            return new CardinalityDecision(CardinalityDecisionKind.Overflow, _options.OverflowKey);
        }

        if (keys.TryAdd(key, 0))
        {
            return new CardinalityDecision(CardinalityDecisionKind.Allowed, key);
        }

        return new CardinalityDecision(CardinalityDecisionKind.AlreadySeen, key);
    }

    /// <summary>
    /// Gets the number of distinct keys currently tracked for <paramref name="dimension"/>.
    /// </summary>
    public int GetTrackedCount(string dimension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dimension);
        return _dimensions.TryGetValue(dimension, out ConcurrentDictionary<string, byte>? keys) ? keys.Count : 0;
    }
}
