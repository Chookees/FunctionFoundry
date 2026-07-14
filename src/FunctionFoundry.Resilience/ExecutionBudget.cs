using System.Diagnostics;

namespace FunctionFoundry.Resilience;

/// <summary>
/// Abstraction for monotonic time used by <see cref="ExecutionBudget"/>.
/// </summary>
public interface IMonotonicClock
{
    /// <summary>
    /// Gets the current monotonic timestamp in ticks compatible with <see cref="Stopwatch"/>.
    /// </summary>
    long GetTimestamp();

    /// <summary>
    /// Gets the frequency of <see cref="GetTimestamp"/> ticks per second.
    /// </summary>
    long Frequency { get; }
}

/// <summary>
/// <see cref="Stopwatch"/>-based monotonic clock.
/// </summary>
public sealed class StopwatchMonotonicClock : IMonotonicClock
{
    /// <inheritdoc />
    public long GetTimestamp() => Stopwatch.GetTimestamp();

    /// <inheritdoc />
    public long Frequency => Stopwatch.Frequency;
}

/// <summary>
/// A reservation against an <see cref="ExecutionBudget"/>.
/// </summary>
/// <param name="ReservationId">Unique reservation identifier.</param>
/// <param name="ReservedDuration">Duration reserved from the budget.</param>
/// <param name="DeadlineTimestamp">Monotonic deadline timestamp for this reservation.</param>
public sealed record BudgetReservation(long ReservationId, TimeSpan ReservedDuration, long DeadlineTimestamp);

/// <summary>
/// Exportable snapshot of budget state.
/// </summary>
/// <param name="TotalBudget">Original total budget duration.</param>
/// <param name="Remaining">Remaining unreserved budget.</param>
/// <param name="ActiveReservations">Number of outstanding reservations.</param>
/// <param name="IsExpired">Whether the overall budget deadline has passed.</param>
public sealed record ExecutionBudgetSnapshot(TimeSpan TotalBudget, TimeSpan Remaining, int ActiveReservations, bool IsExpired);

/// <summary>
/// Monotonic execution budget supporting nested child budgets and reservation cleanup.
/// </summary>
public sealed class ExecutionBudget : IDisposable
{
    private readonly IMonotonicClock _clock;
    private readonly long _startTimestamp;
    private readonly TimeSpan _totalBudget;
    private readonly object _sync = new();
    private TimeSpan _remaining;
    private long _nextReservationId = 1;
    private int _activeReservations;
    private readonly Dictionary<long, TimeSpan> _reservations = new();
    private bool _disposed;

    private ExecutionBudget(IMonotonicClock clock, TimeSpan totalBudget, TimeSpan remaining, long startTimestamp)
    {
        _clock = clock;
        _totalBudget = totalBudget;
        _remaining = remaining;
        _startTimestamp = startTimestamp;
    }

    /// <summary>
    /// Creates a budget with the supplied total duration.
    /// </summary>
    /// <param name="totalBudget">Total available time.</param>
    /// <param name="clock">Optional monotonic clock.</param>
    /// <returns>A new execution budget.</returns>
    public static ExecutionBudget Create(TimeSpan totalBudget, IMonotonicClock? clock = null)
    {
        if (totalBudget <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(totalBudget), "Total budget must be positive.");
        }

        IMonotonicClock resolved = clock ?? new StopwatchMonotonicClock();
        return new ExecutionBudget(resolved, totalBudget, totalBudget, resolved.GetTimestamp());
    }

    /// <summary>
    /// Gets the total budget duration.
    /// </summary>
    public TimeSpan TotalBudget => _totalBudget;

    /// <summary>
    /// Gets whether the overall budget has expired.
    /// </summary>
    public bool IsExpired => Elapsed() >= _totalBudget;

    /// <summary>
    /// Creates a nested child budget capped by <paramref name="maxShare"/>.
    /// </summary>
    /// <param name="maxShare">Maximum duration the child may reserve.</param>
    /// <returns>A child budget sharing the same monotonic clock origin.</returns>
    public ExecutionBudget CreateChild(TimeSpan maxShare)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (maxShare <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maxShare), "Child share must be positive.");
        }

        lock (_sync)
        {
            TimeSpan allowed = TimeSpan.FromTicks(Math.Min(maxShare.Ticks, _remaining.Ticks));
            if (allowed <= TimeSpan.Zero)
            {
                throw new InvalidOperationException("Parent budget has no remaining time for a child budget.");
            }

            return new ExecutionBudget(_clock, allowed, allowed, _startTimestamp);
        }
    }

    /// <summary>
    /// Attempts to reserve up to <paramref name="duration"/> from the budget.
    /// </summary>
    /// <param name="duration">Requested reservation duration.</param>
    /// <param name="reservation">When successful, the reservation handle.</param>
    /// <returns><see langword="true"/> when the reservation was granted.</returns>
    public bool TryReserve(TimeSpan duration, out BudgetReservation reservation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "Reservation duration must be positive.");
        }

        lock (_sync)
        {
            if (IsExpiredLocked())
            {
                reservation = new BudgetReservation(0, TimeSpan.Zero, 0);
                return false;
            }

            TimeSpan remainingOverall = RemainingOverallLocked();
            if (duration > remainingOverall)
            {
                reservation = new BudgetReservation(0, TimeSpan.Zero, 0);
                return false;
            }

            if (duration > _remaining)
            {
                reservation = new BudgetReservation(0, TimeSpan.Zero, 0);
                return false;
            }

            long id = _nextReservationId++;
            _remaining -= duration;
            _activeReservations++;
            _reservations[id] = duration;
            long deadline = _startTimestamp + ToTimestampTicks(duration + ElapsedLocked());
            reservation = new BudgetReservation(id, duration, deadline);
            return true;
        }
    }

    /// <summary>
    /// Releases a reservation and rebalances unused time back into the budget.
    /// </summary>
    /// <param name="reservation">Reservation to release.</param>
    /// <param name="actualUsed">Actual time consumed by the operation.</param>
    public void Release(BudgetReservation reservation, TimeSpan actualUsed)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(reservation);
        ArgumentOutOfRangeException.ThrowIfLessThan(reservation.ReservationId, 1);
        if (actualUsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(actualUsed), "Actual used time cannot be negative.");
        }

        lock (_sync)
        {
            if (!_reservations.Remove(reservation.ReservationId, out TimeSpan reserved))
            {
                throw new InvalidOperationException("Unknown or already released reservation.");
            }

            _activeReservations = Math.Max(0, _activeReservations - 1);
            TimeSpan unused = reserved - actualUsed;
            if (unused > TimeSpan.Zero)
            {
                _remaining += unused;
                if (_remaining > RemainingOverallLocked())
                {
                    _remaining = RemainingOverallLocked();
                }
            }
        }
    }

    /// <summary>
    /// Returns an exportable snapshot of budget state.
    /// </summary>
    public ExecutionBudgetSnapshot GetSnapshot()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_sync)
        {
            return new ExecutionBudgetSnapshot(_totalBudget, _remaining, _activeReservations, IsExpiredLocked());
        }
    }

    /// <summary>
    /// Disposes the budget and invalidates outstanding reservations.
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
            _reservations.Clear();
            _activeReservations = 0;
            _remaining = TimeSpan.Zero;
        }
    }

    private TimeSpan Elapsed()
    {
        return TimeSpan.FromSeconds((double)(ElapsedTicks()) / _clock.Frequency);
    }

    private long ElapsedTicks()
    {
        return _clock.GetTimestamp() - _startTimestamp;
    }

    private TimeSpan ElapsedLocked() => Elapsed();

    private bool IsExpiredLocked() => ElapsedLocked() >= _totalBudget;

    private TimeSpan RemainingOverallLocked()
    {
        TimeSpan elapsed = ElapsedLocked();
        TimeSpan remaining = _totalBudget - elapsed;
        return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }

    private long ToTimestampTicks(TimeSpan duration)
    {
        return (long)(duration.TotalSeconds * _clock.Frequency);
    }
}
