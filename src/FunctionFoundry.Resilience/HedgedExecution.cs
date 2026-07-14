namespace FunctionFoundry.Resilience;

/// <summary>
/// Exportable metrics from <see cref="HedgedExecution"/>.
/// </summary>
/// <param name="TotalExecutions">Total hedged execution attempts started.</param>
/// <param name="PrimarySuccesses">Executions where the primary attempt succeeded first.</param>
/// <param name="HedgeWins">Executions where a hedge attempt won.</param>
/// <param name="AmplificationLimited">Executions where hedging was skipped due to amplification limits.</param>
/// <param name="IdempotencyWarnings">Non-idempotent operations executed with hedging enabled.</param>
/// <param name="AggregatedFailures">Executions where every attempt failed.</param>
/// <param name="ObservedLatencySampleCount">Number of latency samples recorded.</param>
/// <param name="CurrentHedgeDelayMilliseconds">Current hedge delay derived from percentiles.</param>
public sealed record HedgedExecutionMetrics(
    long TotalExecutions,
    long PrimarySuccesses,
    long HedgeWins,
    long AmplificationLimited,
    long IdempotencyWarnings,
    long AggregatedFailures,
    int ObservedLatencySampleCount,
    double CurrentHedgeDelayMilliseconds);

/// <summary>
/// Options for <see cref="HedgedExecution"/>.
/// </summary>
public sealed class HedgedExecutionOptions
{
    /// <summary>
    /// Gets or sets the maximum number of concurrent attempts per execution (including primary). Defaults to 2.
    /// </summary>
    public int MaxAmplification { get; set; } = 2;

    /// <summary>
    /// Gets or sets the percentile used to compute hedge delay. Defaults to 0.95.
    /// </summary>
    public double HedgePercentile { get; set; } = 0.95;

    /// <summary>
    /// Gets or sets the minimum hedge delay. Defaults to 5 ms.
    /// </summary>
    public TimeSpan MinimumHedgeDelay { get; set; } = TimeSpan.FromMilliseconds(5);

    /// <summary>
    /// Gets or sets the maximum latency samples retained for percentile calculation. Defaults to 128.
    /// </summary>
    public int MaxLatencySamples { get; set; } = 128;

    /// <summary>
    /// Gets or sets whether non-idempotent operations should emit warnings. Defaults to <see langword="true"/>.
    /// </summary>
    public bool WarnOnNonIdempotent { get; set; } = true;

    /// <summary>
    /// Validates option ranges.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when numeric options are out of range.</exception>
    public void Validate()
    {
        if (MaxAmplification < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxAmplification), "MaxAmplification must be at least 1.");
        }

        if (HedgePercentile is <= 0 or >= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(HedgePercentile), "HedgePercentile must be in (0, 1).");
        }

        if (MinimumHedgeDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumHedgeDelay), "MinimumHedgeDelay cannot be negative.");
        }

        if (MaxLatencySamples <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxLatencySamples), "MaxLatencySamples must be positive.");
        }
    }
}

/// <summary>
/// Aggregated failures from a hedged execution where every attempt failed.
/// </summary>
public sealed class HedgedExecutionAggregateException : AggregateException
{
    /// <summary>
    /// Initializes a new instance of <see cref="HedgedExecutionAggregateException"/>.
    /// </summary>
    public HedgedExecutionAggregateException()
        : base("Hedged execution failed.")
    {
    }

    /// <summary>
    /// Initializes a new instance with a summary message.
    /// </summary>
    /// <param name="message">Summary message.</param>
    public HedgedExecutionAggregateException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance with a summary message and inner exception.
    /// </summary>
    /// <param name="message">Summary message.</param>
    /// <param name="innerException">Inner exception.</param>
    public HedgedExecutionAggregateException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance with the supplied failures.
    /// </summary>
    /// <param name="message">Summary message.</param>
    /// <param name="innerExceptions">All attempt failures.</param>
    public HedgedExecutionAggregateException(string message, IReadOnlyList<Exception> innerExceptions)
        : base(message, innerExceptions)
    {
    }
}

/// <summary>
/// Executes operations with optional hedged secondary attempts based on rolling latency percentiles.
/// </summary>
public sealed class HedgedExecution
{
    private readonly HedgedExecutionOptions _options;
    private readonly object _sync = new();
    private readonly List<double> _latencySamples = [];
    private long _totalExecutions;
    private long _primarySuccesses;
    private long _hedgeWins;
    private long _amplificationLimited;
    private long _idempotencyWarnings;
    private long _aggregatedFailures;

    /// <summary>
    /// Initializes a new hedged executor.
    /// </summary>
    /// <param name="options">Execution options.</param>
    public HedgedExecution(HedgedExecutionOptions? options = null)
    {
        _options = options ?? new HedgedExecutionOptions();
        _options.Validate();
    }

    /// <summary>
    /// Gets the configured options.
    /// </summary>
    public HedgedExecutionOptions Options => _options;

    /// <summary>
    /// Executes an operation with optional hedging.
    /// </summary>
    /// <typeparam name="T">Result type.</typeparam>
    /// <param name="operation">Operation factory receiving a linked cancellation token.</param>
    /// <param name="isIdempotent">Whether the operation is safe to duplicate.</param>
    /// <param name="validateResult">Optional validator; invalid results are treated as failures.</param>
    /// <param name="cancellationToken">Caller cancellation token.</param>
    /// <returns>The first valid successful result.</returns>
    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        bool isIdempotent,
        Func<T, bool>? validateResult = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        validateResult ??= static _ => true;

        if (!isIdempotent && _options.WarnOnNonIdempotent)
        {
            lock (_sync)
            {
                _idempotencyWarnings++;
            }
        }

        lock (_sync)
        {
            _totalExecutions++;
        }

        int maxAttempts = _options.MaxAmplification;
        if (maxAttempts <= 1)
        {
            lock (_sync)
            {
                _amplificationLimited++;
            }

            return await RunSingleAsync(operation, validateResult, cancellationToken).ConfigureAwait(false);
        }

        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        List<Exception> failures = [];
        TimeSpan hedgeDelay = GetHedgeDelay();
        List<Task<(T Result, int AttemptIndex)>> attempts = [];
        int launched = 0;

        Task<(T Result, int AttemptIndex)> LaunchAttemptAsync(int attemptIndex)
        {
            launched++;
            return RunAttemptAsync(operation, validateResult, attemptIndex, linked.Token);
        }

        attempts.Add(LaunchAttemptAsync(0));
        bool hedgeLaunched = false;

        while (attempts.Count > 0)
        {
            List<Task> waiters = new(attempts);
            Task? hedgeTimer = null;
            if (!hedgeLaunched && launched < maxAttempts)
            {
                hedgeTimer = Task.Delay(hedgeDelay, linked.Token);
                waiters.Add(hedgeTimer);
            }

            Task completed = await Task.WhenAny(waiters).ConfigureAwait(false);
            if (hedgeTimer is not null && completed == hedgeTimer)
            {
                if (hedgeTimer.IsCompletedSuccessfully)
                {
                    attempts.Add(LaunchAttemptAsync(launched));
                    hedgeLaunched = true;
                }

                continue;
            }

            var attemptTask = (Task<(T Result, int AttemptIndex)>)completed;
            if (attemptTask.IsCompletedSuccessfully)
            {
                (T result, int attemptIndex) = await attemptTask.ConfigureAwait(false);
                await linked.CancelAsync().ConfigureAwait(false);
                RecordSuccess(attemptIndex, result);
                return result;
            }

            Exception failure = attemptTask.Exception?.InnerExceptions.FirstOrDefault()
                ?? (Exception?)attemptTask.Exception
                ?? new InvalidOperationException("Hedged attempt failed.");
            failures.Add(failure);
            attempts.Remove(attemptTask);

            if (!hedgeLaunched && launched < maxAttempts)
            {
                attempts.Add(LaunchDelayedAttemptAsync(launched, hedgeDelay));
                hedgeLaunched = true;
            }
        }

        async Task<(T Result, int AttemptIndex)> LaunchDelayedAttemptAsync(int attemptIndex, TimeSpan delay)
        {
            await Task.Delay(delay, linked.Token).ConfigureAwait(false);
            return await RunAttemptAsync(operation, validateResult, attemptIndex, linked.Token).ConfigureAwait(false);
        }

        lock (_sync)
        {
            _aggregatedFailures++;
        }

        throw new HedgedExecutionAggregateException("All hedged attempts failed.", failures);
    }

    /// <summary>
    /// Returns exportable execution metrics.
    /// </summary>
    public HedgedExecutionMetrics GetMetrics()
    {
        lock (_sync)
        {
            return new HedgedExecutionMetrics(
                _totalExecutions,
                _primarySuccesses,
                _hedgeWins,
                _amplificationLimited,
                _idempotencyWarnings,
                _aggregatedFailures,
                _latencySamples.Count,
                GetHedgeDelayLocked().TotalMilliseconds);
        }
    }

    private async Task<T> RunSingleAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        Func<T, bool> validateResult,
        CancellationToken cancellationToken)
    {
        long start = Environment.TickCount64;
        try
        {
            T result = await operation(cancellationToken).ConfigureAwait(false);
            if (!validateResult(result))
            {
                throw new InvalidOperationException("Operation returned an invalid result.");
            }

            RecordLatency(TimeSpan.FromMilliseconds(Environment.TickCount64 - start));
            lock (_sync)
            {
                _primarySuccesses++;
            }

            return result;
        }
        catch
        {
            RecordLatency(TimeSpan.FromMilliseconds(Environment.TickCount64 - start));
            throw;
        }
    }

    private async Task<(T Result, int AttemptIndex)> RunAttemptAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        Func<T, bool> validateResult,
        int attemptIndex,
        CancellationToken cancellationToken)
    {
        long start = Environment.TickCount64;
        try
        {
            T result = await operation(cancellationToken).ConfigureAwait(false);
            if (!validateResult(result))
            {
                throw new InvalidOperationException("Operation returned an invalid result.");
            }

            RecordLatency(TimeSpan.FromMilliseconds(Environment.TickCount64 - start));
            return (result, attemptIndex);
        }
        catch
        {
            RecordLatency(TimeSpan.FromMilliseconds(Environment.TickCount64 - start));
            throw;
        }
    }

    private void RecordSuccess<T>(int attemptIndex, T result)
    {
        _ = result;
        lock (_sync)
        {
            if (attemptIndex == 0)
            {
                _primarySuccesses++;
            }
            else
            {
                _hedgeWins++;
            }
        }
    }

    private void RecordLatency(TimeSpan latency)
    {
        lock (_sync)
        {
            _latencySamples.Add(latency.TotalMilliseconds);
            if (_latencySamples.Count > _options.MaxLatencySamples)
            {
                _latencySamples.RemoveAt(0);
            }
        }
    }

    private TimeSpan GetHedgeDelay()
    {
        lock (_sync)
        {
            return GetHedgeDelayLocked();
        }
    }

    private TimeSpan GetHedgeDelayLocked()
    {
        if (_latencySamples.Count == 0)
        {
            return _options.MinimumHedgeDelay;
        }

        double[] sorted = _latencySamples.ToArray();
        Array.Sort(sorted);
        int index = (int)Math.Ceiling(_options.HedgePercentile * sorted.Length) - 1;
        index = Math.Clamp(index, 0, sorted.Length - 1);
        double milliseconds = Math.Max(_options.MinimumHedgeDelay.TotalMilliseconds, sorted[index]);
        return TimeSpan.FromMilliseconds(milliseconds);
    }
}
