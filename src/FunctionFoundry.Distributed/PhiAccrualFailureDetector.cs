namespace FunctionFoundry.Distributed;

/// <summary>
/// Abstraction over wall-clock readings for failure detection and simulation.
/// </summary>
public interface IClock
{
    /// <summary>
    /// Gets the current UTC time in milliseconds since Unix epoch.
    /// </summary>
    long UnixTimeMilliseconds { get; }
}

/// <summary>
/// Production clock backed by <see cref="DateTimeOffset.UtcNow"/>.
/// </summary>
public sealed class SystemClock : IClock
{
    /// <inheritdoc />
    public long UnixTimeMilliseconds => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

/// <summary>
/// Mutable clock for deterministic simulations and tests.
/// </summary>
public sealed class SimulatedClock : IClock
{
    /// <summary>
    /// Initializes a simulated clock at the supplied instant.
    /// </summary>
    /// <param name="unixTimeMilliseconds">Initial UTC milliseconds since Unix epoch.</param>
    public SimulatedClock(long unixTimeMilliseconds = 0) => UnixTimeMilliseconds = unixTimeMilliseconds;

    /// <inheritdoc />
    public long UnixTimeMilliseconds { get; set; }

    /// <summary>
    /// Advances the simulated clock by <paramref name="milliseconds"/>.
    /// </summary>
    /// <param name="milliseconds">Milliseconds to advance. Must be non-negative.</param>
    public void Advance(long milliseconds)
    {
        if (milliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(milliseconds), "Advance amount cannot be negative.");
        }

        UnixTimeMilliseconds += milliseconds;
    }
}

/// <summary>
/// Options controlling phi-accrual failure detection.
/// </summary>
public sealed class PhiAccrualFailureDetectorOptions
{
    /// <summary>
    /// Gets or sets the acceptable heartbeat pause in milliseconds before suspicion rises. Defaults to 5_000.
    /// </summary>
    public long AcceptableHeartbeatPauseMilliseconds { get; set; } = 5_000;

    /// <summary>
    /// Gets or sets the number of heartbeat intervals required for warm-up. Defaults to 8.
    /// </summary>
    public int WarmupIntervals { get; set; } = 8;

    /// <summary>
    /// Gets or sets the maximum retained heartbeat intervals. Defaults to 200.
    /// </summary>
    public int MaxSampleSize { get; set; } = 200;

    /// <summary>
    /// Gets or sets the phi threshold treated as unavailable. Defaults to 8.0.
    /// </summary>
    public double PhiThreshold { get; set; } = 8.0;

    /// <summary>
    /// Gets or sets the multiplier applied to the standard deviation when classifying outlier intervals. Defaults to 3.0.
    /// </summary>
    public double OutlierStandardDeviationMultiplier { get; set; } = 3.0;

    /// <summary>
    /// Validates option ranges.
    /// </summary>
    public void Validate()
    {
        if (AcceptableHeartbeatPauseMilliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(AcceptableHeartbeatPauseMilliseconds), "Pause must be positive.");
        }

        if (WarmupIntervals <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(WarmupIntervals), "WarmupIntervals must be positive.");
        }

        if (MaxSampleSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxSampleSize), "MaxSampleSize must be positive.");
        }

        if (PhiThreshold <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(PhiThreshold), "PhiThreshold must be positive.");
        }

        if (OutlierStandardDeviationMultiplier <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(OutlierStandardDeviationMultiplier), "Multiplier must be positive.");
        }
    }
}

/// <summary>
/// Snapshot of phi-accrual detector state.
/// </summary>
/// <param name="Phi">Current suspicion score.</param>
/// <param name="IsAvailable">Whether phi is below the configured threshold.</param>
/// <param name="IsWarmingUp">Whether warm-up samples are still being collected.</param>
/// <param name="LastHeartbeatUnixMilliseconds">Last recorded heartbeat timestamp, if any.</param>
/// <param name="SampleCount">Number of retained inter-arrival samples.</param>
public sealed record PhiAccrualSnapshot(
    double Phi,
    bool IsAvailable,
    bool IsWarmingUp,
    long? LastHeartbeatUnixMilliseconds,
    int SampleCount);

/// <summary>
/// Phi-accrual failure detector using heartbeat inter-arrival statistics.
/// </summary>
/// <remarks>
/// <para>Suspicion is statistical evidence derived from heartbeat timing; it is not proof of failure.</para>
/// <para>Outlier intervals beyond mean + multiplier * standard deviation are excluded from distribution statistics.</para>
/// <para>During warm-up the detector reports availability and zero phi until enough samples are collected.</para>
/// </remarks>
public sealed class PhiAccrualFailureDetector
{
    private readonly IClock _clock;
    private readonly PhiAccrualFailureDetectorOptions _options;
    private readonly Queue<double> _intervalsMs = new();
    private long? _lastHeartbeatUnixMilliseconds;

    /// <summary>
    /// Initializes a new phi-accrual detector.
    /// </summary>
    /// <param name="clock">Clock used for timestamps. Must not be null.</param>
    /// <param name="options">Optional detector options.</param>
    public PhiAccrualFailureDetector(IClock clock, PhiAccrualFailureDetectorOptions? options = null)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _options = options ?? new PhiAccrualFailureDetectorOptions();
        _options.Validate();
    }

    /// <summary>
    /// Records a heartbeat at the current clock time.
    /// </summary>
    public void RecordHeartbeat()
    {
        long now = _clock.UnixTimeMilliseconds;
        if (_lastHeartbeatUnixMilliseconds is long last)
        {
            double interval = now - last;
            if (interval > 0)
            {
                AddInterval(interval);
            }
        }

        _lastHeartbeatUnixMilliseconds = now;
    }

    /// <summary>
    /// Records a heartbeat at an explicit timestamp for deterministic simulation.
    /// </summary>
    /// <param name="unixTimeMilliseconds">Heartbeat timestamp in UTC milliseconds since Unix epoch.</param>
    public void RecordHeartbeatAt(long unixTimeMilliseconds)
    {
        if (_lastHeartbeatUnixMilliseconds is long last)
        {
            double interval = unixTimeMilliseconds - last;
            if (interval > 0)
            {
                AddInterval(interval);
            }
        }

        _lastHeartbeatUnixMilliseconds = unixTimeMilliseconds;
    }

    /// <summary>
    /// Computes the current phi suspicion score.
    /// </summary>
    /// <returns>Phi value where higher values indicate stronger suspicion of unavailability.</returns>
    public double ComputePhi()
    {
        PhiAccrualSnapshot snapshot = GetSnapshot();
        return snapshot.Phi;
    }

    /// <summary>
    /// Gets the current detector snapshot.
    /// </summary>
    /// <returns>Snapshot including phi, availability, and warm-up state.</returns>
    public PhiAccrualSnapshot GetSnapshot()
    {
        if (_lastHeartbeatUnixMilliseconds is not long lastHeartbeat)
        {
            return new PhiAccrualSnapshot(0, true, true, null, _intervalsMs.Count);
        }

        if (_intervalsMs.Count < _options.WarmupIntervals)
        {
            return new PhiAccrualSnapshot(0, true, true, lastHeartbeat, _intervalsMs.Count);
        }

        double elapsed = _clock.UnixTimeMilliseconds - lastHeartbeat;
        if (elapsed <= 0)
        {
            return new PhiAccrualSnapshot(0, true, false, lastHeartbeat, _intervalsMs.Count);
        }

        double[] filtered = FilterOutliers(_intervalsMs);
        if (filtered.Length == 0)
        {
            return new PhiAccrualSnapshot(0, true, false, lastHeartbeat, _intervalsMs.Count);
        }

        double mean = filtered.Average();
        double variance = filtered.Select(v => (v - mean) * (v - mean)).Average();
        double stdDev = Math.Sqrt(variance);
        if (stdDev <= double.Epsilon)
        {
            stdDev = mean * 0.1 + 1.0;
        }

        double cdf = NormalCdf(elapsed, mean, stdDev);
        double probability = Math.Max(1.0 - cdf, 1e-12);
        double phi = -Math.Log10(probability);
        bool available = phi < _options.PhiThreshold;
        return new PhiAccrualSnapshot(phi, available, false, lastHeartbeat, _intervalsMs.Count);
    }

    private void AddInterval(double intervalMs)
    {
        _intervalsMs.Enqueue(intervalMs);
        while (_intervalsMs.Count > _options.MaxSampleSize)
        {
            _ = _intervalsMs.Dequeue();
        }
    }

    private double[] FilterOutliers(IEnumerable<double> intervals)
    {
        double[] values = intervals.ToArray();
        if (values.Length == 0)
        {
            return values;
        }

        double mean = values.Average();
        double variance = values.Select(v => (v - mean) * (v - mean)).Average();
        double stdDev = Math.Sqrt(variance);
        double limit = mean + (_options.OutlierStandardDeviationMultiplier * stdDev);
        return values.Where(v => v <= limit).ToArray();
    }

    private static double NormalCdf(double value, double mean, double stdDev)
    {
        double z = (value - mean) / stdDev;
        return 0.5 * (1.0 + Erf(z / Math.Sqrt(2.0)));
    }

    private static double Erf(double x)
    {
        // Abramowitz and Stegun approximation 7.1.26
        double sign = x < 0 ? -1.0 : 1.0;
        double ax = Math.Abs(x);
        double t = 1.0 / (1.0 + (0.3275911 * ax));
        double y = 1.0 - (((((1.061405429 * t) - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t * Math.Exp(-ax * ax);
        return sign * y;
    }
}
