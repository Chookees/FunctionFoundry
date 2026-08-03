namespace FunctionFoundry.Resilience;

/// <summary>
/// Options for <see cref="RetryBudget"/>.
/// </summary>
public sealed class RetryBudgetOptions
{
    /// <summary>
    /// Gets or sets tokens added back per successful operation. Defaults to 0.1.
    /// </summary>
    public double RetryRatio { get; set; } = 0.1;

    /// <summary>
    /// Gets or sets the minimum refill rate in tokens per second. Defaults to 1.
    /// </summary>
    public double MinRetriesPerSecond { get; set; } = 1;

    /// <summary>
    /// Gets or sets the maximum accumulated budget tokens. Defaults to 100.
    /// </summary>
    public double MaxBudgetTokens { get; set; } = 100;

    /// <summary>
    /// Validates options.
    /// </summary>
    public void Validate()
    {
        if (RetryRatio < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(RetryRatio), "RetryRatio cannot be negative.");
        }

        if (MinRetriesPerSecond < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MinRetriesPerSecond), "MinRetriesPerSecond cannot be negative.");
        }

        if (MaxBudgetTokens <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxBudgetTokens), "MaxBudgetTokens must be positive.");
        }
    }
}

/// <summary>
/// Optional clock abstraction for <see cref="RetryBudget"/> tests; production uses UTC wall time.
/// </summary>
public interface IRetryBudgetClock
{
    /// <summary>UTC milliseconds since Unix epoch.</summary>
    long UnixTimeMilliseconds { get; }
}

/// <summary>
/// Token-budget gate that admits retries without letting them amplify outages.
/// </summary>
/// <remarks>
/// <para>Each retry consumes one token. Successes and elapsed time refill the budget up to <see cref="RetryBudgetOptions.MaxBudgetTokens"/>.</para>
/// <para>Thread safety: instance methods are synchronized.</para>
/// </remarks>
public sealed class RetryBudget
{
    private readonly RetryBudgetOptions _options;
    private readonly IRetryBudgetClock _clock;
    private readonly object _gate = new();
    private double _tokens;
    private long _lastRefillUnixMs;

    private sealed class SystemUtcClock : IRetryBudgetClock
    {
        public long UnixTimeMilliseconds => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    /// <summary>
    /// Initializes a new retry budget.
    /// </summary>
    /// <param name="options">Budget options.</param>
    /// <param name="clock">Optional clock for deterministic tests.</param>
    public RetryBudget(RetryBudgetOptions? options = null, IRetryBudgetClock? clock = null)
    {
        _options = options ?? new RetryBudgetOptions();
        _options.Validate();
        _clock = clock ?? new SystemUtcClock();
        _tokens = _options.MaxBudgetTokens;
        _lastRefillUnixMs = _clock.UnixTimeMilliseconds;
    }

    /// <summary>
    /// Gets the current token balance after applying time-based refill.
    /// </summary>
    public double CurrentTokens
    {
        get
        {
            lock (_gate)
            {
                RefillUnlocked();
                return _tokens;
            }
        }
    }

    /// <summary>
    /// Attempts to consume one retry token.
    /// </summary>
    /// <returns><see langword="true"/> when a retry is admitted.</returns>
    public bool TryAcquireRetry()
    {
        lock (_gate)
        {
            RefillUnlocked();
            if (_tokens < 1d)
            {
                return false;
            }

            _tokens -= 1d;
            return true;
        }
    }

    /// <summary>
    /// Records a successful primary/retry outcome and replenishes tokens by <see cref="RetryBudgetOptions.RetryRatio"/>.
    /// </summary>
    public void RecordSuccess()
    {
        lock (_gate)
        {
            RefillUnlocked();
            _tokens = Math.Min(_options.MaxBudgetTokens, _tokens + _options.RetryRatio);
        }
    }

    private void RefillUnlocked()
    {
        long now = _clock.UnixTimeMilliseconds;
        long elapsedMs = Math.Max(0, now - _lastRefillUnixMs);
        if (elapsedMs == 0)
        {
            return;
        }

        double refill = (elapsedMs / 1000d) * _options.MinRetriesPerSecond;
        _tokens = Math.Min(_options.MaxBudgetTokens, _tokens + refill);
        _lastRefillUnixMs = now;
    }
}
