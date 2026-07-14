using FunctionFoundry.Resilience;

namespace FunctionFoundry.Resilience.Tests;

public sealed class ExecutionBudgetTests
{
    private sealed class FakeClock : IMonotonicClock
    {
        public long Ticks { get; private set; }

        public long Frequency => 1_000;

        public long GetTimestamp() => Ticks;

        public void Advance(TimeSpan duration) => Ticks += (long)duration.TotalMilliseconds;
    }

    [Fact]
    public void Reserve_and_release_rebalances_unused_time()
    {
        var clock = new FakeClock();
        using ExecutionBudget budget = ExecutionBudget.Create(TimeSpan.FromSeconds(10), clock);
        Assert.True(budget.TryReserve(TimeSpan.FromSeconds(4), out BudgetReservation reservation));

        budget.Release(reservation, TimeSpan.FromSeconds(1));
        ExecutionBudgetSnapshot snapshot = budget.GetSnapshot();
        Assert.Equal(TimeSpan.FromSeconds(9), snapshot.Remaining);
    }

    [Fact]
    public void Impossible_allocation_is_rejected()
    {
        using ExecutionBudget budget = ExecutionBudget.Create(TimeSpan.FromSeconds(1));
        Assert.False(budget.TryReserve(TimeSpan.FromSeconds(2), out _));
    }

    [Fact]
    public void Child_budget_cannot_exceed_parent_remaining()
    {
        using ExecutionBudget parent = ExecutionBudget.Create(TimeSpan.FromSeconds(5));
        Assert.True(parent.TryReserve(TimeSpan.FromSeconds(4), out BudgetReservation reservation));
        using ExecutionBudget child = parent.CreateChild(TimeSpan.FromSeconds(3));
        Assert.Equal(TimeSpan.FromSeconds(1), child.GetSnapshot().Remaining);
        parent.Release(reservation, TimeSpan.FromSeconds(4));
    }

    [Fact]
    public void Monotonic_clock_advances_expiration()
    {
        var clock = new FakeClock();
        using ExecutionBudget budget = ExecutionBudget.Create(TimeSpan.FromMilliseconds(100), clock);
        clock.Advance(TimeSpan.FromMilliseconds(150));
        Assert.True(budget.IsExpired);
    }
}
