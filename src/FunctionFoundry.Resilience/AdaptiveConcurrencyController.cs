namespace FunctionFoundry.Resilience;

/// <summary>
/// Feedback from a completed operation used to adapt concurrency.
/// </summary>
/// <param name="Succeeded">Whether the operation completed successfully.</param>
/// <param name="Latency">Observed end-to-end latency.</param>
public sealed record OperationFeedback(bool Succeeded, TimeSpan Latency);

/// <summary>
/// Exportable snapshot of <see cref="AdaptiveConcurrencyController"/> state.
/// </summary>
/// <param name="CurrentConcurrencyLimit">Current AIMD concurrency limit.</param>
/// <param name="ActivePermits">Number of permits currently held.</param>
/// <param name="QueuedWaiters">Number of callers waiting for a permit.</param>
/// <param name="CompletedOperations">Total completed operations since creation.</param>
/// <param name="SuccessfulOperations">Total successful operations.</param>
/// <param name="RejectedDueToQueueLimit">Times acquisition was rejected because the queue was full.</param>
/// <param name="IsWarmUpComplete">Whether warm-up has finished.</param>
/// <param name="AverageLatencyMilliseconds">Rolling average latency in milliseconds.</param>
/// <param name="ErrorRate">Rolling error rate in the range [0, 1].</param>
public sealed record AdaptiveConcurrencySnapshot(
    int CurrentConcurrencyLimit,
    int ActivePermits,
    int QueuedWaiters,
    long CompletedOperations,
    long SuccessfulOperations,
    long RejectedDueToQueueLimit,
    bool IsWarmUpComplete,
    double AverageLatencyMilliseconds,
    double ErrorRate);

/// <summary>
/// Options for <see cref="AdaptiveConcurrencyController"/>.
/// </summary>
public sealed class AdaptiveConcurrencyControllerOptions
{
    /// <summary>
    /// Gets or sets the minimum concurrency limit. Defaults to 1.
    /// </summary>
    public int MinConcurrency { get; set; } = 1;

    /// <summary>
    /// Gets or sets the maximum concurrency limit. Defaults to 64.
    /// </summary>
    public int MaxConcurrency { get; set; } = 64;

    /// <summary>
    /// Gets or sets the maximum number of queued acquire waiters. Defaults to 256.
    /// </summary>
    public int MaxQueueDepth { get; set; } = 256;

    /// <summary>
    /// Gets or sets the concurrency used during warm-up. Defaults to 4.
    /// </summary>
    public int WarmUpConcurrency { get; set; } = 4;

    /// <summary>
    /// Gets or sets the number of completed operations before warm-up ends. Defaults to 32.
    /// </summary>
    public long WarmUpOperationCount { get; set; } = 32;

    /// <summary>
    /// Gets or sets the target latency in milliseconds for AIMD decisions. Defaults to 100.
    /// </summary>
    public double TargetLatencyMilliseconds { get; set; } = 100;

    /// <summary>
    /// Gets or sets the multiplicative decrease factor applied on high latency or errors. Defaults to 0.5.
    /// </summary>
    public double MultiplicativeDecreaseFactor { get; set; } = 0.5;

    /// <summary>
    /// Gets or sets the additive increase step when latency is healthy. Defaults to 1.
    /// </summary>
    public int AdditiveIncreaseStep { get; set; } = 1;

    /// <summary>
    /// Gets or sets the error-rate threshold that triggers multiplicative decrease. Defaults to 0.1.
    /// </summary>
    public double ErrorRateDecreaseThreshold { get; set; } = 0.1;

    /// <summary>
    /// Gets or sets the rolling window size for latency and error statistics. Defaults to 64.
    /// </summary>
    public int RollingWindowSize { get; set; } = 64;

    /// <summary>
    /// Validates option ranges.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when numeric options are out of range.</exception>
    public void Validate()
    {
        if (MinConcurrency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MinConcurrency), "MinConcurrency must be positive.");
        }

        if (MaxConcurrency < MinConcurrency)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxConcurrency), "MaxConcurrency must be >= MinConcurrency.");
        }

        if (MaxQueueDepth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxQueueDepth), "MaxQueueDepth cannot be negative.");
        }

        if (WarmUpConcurrency < MinConcurrency || WarmUpConcurrency > MaxConcurrency)
        {
            throw new ArgumentOutOfRangeException(nameof(WarmUpConcurrency), "WarmUpConcurrency must be within [MinConcurrency, MaxConcurrency].");
        }

        if (WarmUpOperationCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(WarmUpOperationCount), "WarmUpOperationCount cannot be negative.");
        }

        if (TargetLatencyMilliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(TargetLatencyMilliseconds), "TargetLatencyMilliseconds must be positive.");
        }

        if (MultiplicativeDecreaseFactor is <= 0 or >= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MultiplicativeDecreaseFactor), "MultiplicativeDecreaseFactor must be in (0, 1).");
        }

        if (AdditiveIncreaseStep <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(AdditiveIncreaseStep), "AdditiveIncreaseStep must be positive.");
        }

        if (ErrorRateDecreaseThreshold is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(ErrorRateDecreaseThreshold), "ErrorRateDecreaseThreshold must be in [0, 1].");
        }

        if (RollingWindowSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(RollingWindowSize), "RollingWindowSize must be positive.");
        }
    }
}

/// <summary>
/// A permit acquired from <see cref="AdaptiveConcurrencyController"/>.
/// </summary>
/// <param name="PermitId">Monotonic identifier for diagnostics.</param>
public sealed record ConcurrencyPermit(long PermitId);

/// <summary>
/// Result of a permit acquisition attempt.
/// </summary>
/// <param name="Acquired">Whether a permit was acquired.</param>
/// <param name="Permit">The acquired permit when <paramref name="Acquired"/> is <see langword="true"/>.</param>
public readonly record struct AcquireAttemptResult(bool Acquired, ConcurrencyPermit Permit);

/// <summary>
/// AIMD concurrency controller with latency and error feedback.
/// </summary>
public sealed class AdaptiveConcurrencyController : IDisposable
{
    private readonly AdaptiveConcurrencyControllerOptions _options;
    private readonly object _sync = new();
    private readonly Queue<TaskCompletionSource<ConcurrencyPermit>> _waiters = new();
    private readonly Queue<double> _latencyWindow = new();
    private readonly Queue<bool> _successWindow = new();
    private int _currentLimit;
    private int _activePermits;
    private long _nextPermitId = 1;
    private long _completedOperations;
    private long _successfulOperations;
    private long _rejectedDueToQueueLimit;
    private bool _disposed;

    /// <summary>
    /// Initializes a new controller.
    /// </summary>
    /// <param name="options">Controller options.</param>
    public AdaptiveConcurrencyController(AdaptiveConcurrencyControllerOptions? options = null)
    {
        _options = options ?? new AdaptiveConcurrencyControllerOptions();
        _options.Validate();
        _currentLimit = _options.WarmUpConcurrency;
    }

    /// <summary>
    /// Gets the configured options.
    /// </summary>
    public AdaptiveConcurrencyControllerOptions Options => _options;

    /// <summary>
    /// Attempts to acquire a concurrency permit, waiting when necessary up to <paramref name="timeout"/>.
    /// </summary>
    /// <param name="timeout">Maximum time to wait for a permit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Acquisition result.</returns>
    public async ValueTask<AcquireAttemptResult> TryAcquireAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (timeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout cannot be negative.");
        }

        TaskCompletionSource<ConcurrencyPermit> waiter = CreateWaiter();
        lock (_sync)
        {
            if (TryGrantPermitLocked(out ConcurrencyPermit granted))
            {
                return new AcquireAttemptResult(true, granted);
            }

            if (_waiters.Count >= _options.MaxQueueDepth)
            {
                _rejectedDueToQueueLimit++;
                return new AcquireAttemptResult(false, new ConcurrencyPermit(0));
            }

            _waiters.Enqueue(waiter);
        }

        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task delayTask = Task.Delay(timeout, linked.Token);
        Task<ConcurrencyPermit> acquireTask = waiter.Task;
        Task completed = await Task.WhenAny(acquireTask, delayTask).ConfigureAwait(false);
        if (completed == acquireTask)
        {
            ConcurrencyPermit permit = await acquireTask.ConfigureAwait(false);
            return new AcquireAttemptResult(true, permit);
        }

        await linked.CancelAsync().ConfigureAwait(false);
        RemoveWaiter(waiter);
        return new AcquireAttemptResult(false, new ConcurrencyPermit(0));
    }

    /// <summary>
    /// Releases a permit and records operation feedback for AIMD adaptation.
    /// </summary>
    /// <param name="permit">The permit to release.</param>
    /// <param name="feedback">Observed operation feedback.</param>
    public void Release(ConcurrencyPermit permit, OperationFeedback feedback)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(feedback);
        ArgumentNullException.ThrowIfNull(permit);
        ArgumentOutOfRangeException.ThrowIfLessThan(permit.PermitId, 1);

        lock (_sync)
        {
            if (_activePermits > 0)
            {
                _activePermits--;
            }

            RecordFeedbackLocked(feedback);
            AdaptLocked();
            GrantFromQueueLocked();
        }
    }

    /// <summary>
    /// Returns an exportable snapshot of controller state.
    /// </summary>
    public AdaptiveConcurrencySnapshot GetSnapshot()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_sync)
        {
            return new AdaptiveConcurrencySnapshot(
                _currentLimit,
                _activePermits,
                _waiters.Count,
                _completedOperations,
                _successfulOperations,
                _rejectedDueToQueueLimit,
                IsWarmUpCompleteLocked(),
                ComputeAverageLatencyLocked(),
                ComputeErrorRateLocked());
        }
    }

    /// <summary>
    /// Applies feedback without acquiring or releasing permits. Intended for deterministic simulation tests.
    /// </summary>
    /// <param name="feedback">Observed operation feedback.</param>
    public void SimulateFeedback(OperationFeedback feedback)
    {
        SimulateFeedbackBatch([feedback]);
    }

    /// <summary>
    /// Applies a batch of feedback samples and runs a single AIMD adaptation pass.
    /// </summary>
    /// <param name="feedback">Observed operation feedback samples.</param>
    public void SimulateFeedbackBatch(IReadOnlyList<OperationFeedback> feedback)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(feedback);
        lock (_sync)
        {
            foreach (OperationFeedback sample in feedback)
            {
                ArgumentNullException.ThrowIfNull(sample);
                RecordFeedbackLocked(sample);
            }

            AdaptLocked();
        }
    }

    /// <summary>
    /// Releases resources and cancels queued waiters.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_sync)
        {
            while (_waiters.Count > 0)
            {
                TaskCompletionSource<ConcurrencyPermit> waiter = _waiters.Dequeue();
                waiter.TrySetCanceled();
            }
        }
    }

    private static TaskCompletionSource<ConcurrencyPermit> CreateWaiter()
    {
        return new TaskCompletionSource<ConcurrencyPermit>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private bool TryGrantPermitLocked(out ConcurrencyPermit permit)
    {
        if (_activePermits < EffectiveLimitLocked())
        {
            _activePermits++;
            permit = new ConcurrencyPermit(_nextPermitId++);
            return true;
        }

        permit = new ConcurrencyPermit(0);
        return false;
    }

    private void GrantFromQueueLocked()
    {
        while (_waiters.Count > 0 && _activePermits < EffectiveLimitLocked())
        {
            TaskCompletionSource<ConcurrencyPermit> waiter = _waiters.Dequeue();
            _activePermits++;
            waiter.TrySetResult(new ConcurrencyPermit(_nextPermitId++));
        }
    }

    private void RemoveWaiter(TaskCompletionSource<ConcurrencyPermit> waiter)
    {
        lock (_sync)
        {
            if (_waiters.Count == 0)
            {
                return;
            }

            int count = _waiters.Count;
            for (int i = 0; i < count; i++)
            {
                TaskCompletionSource<ConcurrencyPermit> current = _waiters.Dequeue();
                if (!ReferenceEquals(current, waiter))
                {
                    _waiters.Enqueue(current);
                }
            }
        }

        waiter.TrySetCanceled();
    }

    private int EffectiveLimitLocked()
    {
        return IsWarmUpCompleteLocked() ? _currentLimit : _options.WarmUpConcurrency;
    }

    private bool IsWarmUpCompleteLocked()
    {
        return _completedOperations >= _options.WarmUpOperationCount;
    }

    private void RecordFeedbackLocked(OperationFeedback feedback)
    {
        _completedOperations++;
        if (feedback.Succeeded)
        {
            _successfulOperations++;
        }

        EnqueueRolling(_latencyWindow, feedback.Latency.TotalMilliseconds);
        EnqueueRolling(_successWindow, feedback.Succeeded);
    }

    private void EnqueueRolling<T>(Queue<T> window, T value)
    {
        window.Enqueue(value);
        while (window.Count > _options.RollingWindowSize)
        {
            window.Dequeue();
        }
    }

    private void AdaptLocked()
    {
        if (!IsWarmUpCompleteLocked())
        {
            return;
        }

        double errorRate = ComputeErrorRateLocked();
        double averageLatency = ComputeAverageLatencyLocked();
        if (errorRate > _options.ErrorRateDecreaseThreshold || averageLatency > _options.TargetLatencyMilliseconds)
        {
            int decreased = (int)Math.Max(
                _options.MinConcurrency,
                Math.Floor(_currentLimit * _options.MultiplicativeDecreaseFactor));
            _currentLimit = decreased;
            return;
        }

        if (averageLatency <= _options.TargetLatencyMilliseconds)
        {
            _currentLimit = Math.Min(_options.MaxConcurrency, _currentLimit + _options.AdditiveIncreaseStep);
        }
    }

    private double ComputeAverageLatencyLocked()
    {
        if (_latencyWindow.Count == 0)
        {
            return 0;
        }

        double sum = 0;
        foreach (double value in _latencyWindow)
        {
            sum += value;
        }

        return sum / _latencyWindow.Count;
    }

    private double ComputeErrorRateLocked()
    {
        if (_successWindow.Count == 0)
        {
            return 0;
        }

        int failures = 0;
        foreach (bool success in _successWindow)
        {
            if (!success)
            {
                failures++;
            }
        }

        return failures / (double)_successWindow.Count;
    }
}
