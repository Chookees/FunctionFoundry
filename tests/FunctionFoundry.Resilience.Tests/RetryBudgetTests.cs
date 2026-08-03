using FunctionFoundry.Resilience;

namespace FunctionFoundry.Resilience.Tests;

public sealed class RetryBudgetTests
{
    private sealed class FixedClock : IRetryBudgetClock
    {
        public long UnixTimeMilliseconds { get; set; }
    }

    [Fact]
    public void Exhausts_budget_then_refills_on_success_and_time()
    {
        var clock = new FixedClock { UnixTimeMilliseconds = 0 };
        var budget = new RetryBudget(
            new RetryBudgetOptions { MaxBudgetTokens = 2, RetryRatio = 1, MinRetriesPerSecond = 10 },
            clock);

        Assert.True(budget.TryAcquireRetry());
        Assert.True(budget.TryAcquireRetry());
        Assert.False(budget.TryAcquireRetry());

        budget.RecordSuccess();
        Assert.True(budget.TryAcquireRetry());

        Assert.False(budget.TryAcquireRetry());
        clock.UnixTimeMilliseconds = 1_000;
        Assert.True(budget.TryAcquireRetry());
    }

    [Fact]
    public void Options_validate()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RetryBudgetOptions { MaxBudgetTokens = 0 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new RetryBudgetOptions { RetryRatio = -1 }.Validate());
    }
}
