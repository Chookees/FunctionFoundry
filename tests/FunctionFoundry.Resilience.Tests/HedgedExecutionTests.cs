using FunctionFoundry.Resilience;

namespace FunctionFoundry.Resilience.Tests;

public sealed class HedgedExecutionTests
{
    [Fact]
    public async Task Primary_success_avoids_hedge()
    {
        var executor = new HedgedExecution(new HedgedExecutionOptions { MaxAmplification = 2, MinimumHedgeDelay = TimeSpan.FromMilliseconds(50) });
        int calls = 0;
        int result = await executor.ExecuteAsync(
            _ =>
            {
                calls++;
                return Task.FromResult(42);
            },
            isIdempotent: true);

        Assert.Equal(42, result);
        Assert.Equal(1, calls);
        Assert.Equal(1, executor.GetMetrics().PrimarySuccesses);
    }

    [Fact]
    public async Task Hedge_wins_when_primary_is_slow()
    {
        var executor = new HedgedExecution(new HedgedExecutionOptions
        {
            MaxAmplification = 2,
            MinimumHedgeDelay = TimeSpan.FromMilliseconds(5),
            HedgePercentile = 0.5,
        });

        int calls = 0;
        int result = await executor.ExecuteAsync(
            async ct =>
            {
                int current = Interlocked.Increment(ref calls);
                if (current == 1)
                {
                    await Task.Delay(200, ct);
                    return 1;
                }

                return 2;
            },
            isIdempotent: true);

        Assert.Equal(2, result);
        Assert.Equal(2, calls);
        Assert.Equal(1, executor.GetMetrics().HedgeWins);
    }

    [Fact]
    public async Task All_failures_aggregate()
    {
        var executor = new HedgedExecution(new HedgedExecutionOptions { MaxAmplification = 2, MinimumHedgeDelay = TimeSpan.FromMilliseconds(1) });
        HedgedExecutionAggregateException ex = await Assert.ThrowsAsync<HedgedExecutionAggregateException>(() =>
            executor.ExecuteAsync<int>(
                _ => throw new InvalidOperationException("boom"),
                isIdempotent: true));

        Assert.Equal(2, ex.InnerExceptions.Count);
        Assert.Equal(1, executor.GetMetrics().AggregatedFailures);
    }

    [Fact]
    public async Task Non_idempotent_operation_records_warning()
    {
        var executor = new HedgedExecution();
        _ = await executor.ExecuteAsync(_ => Task.FromResult("ok"), isIdempotent: false);
        Assert.Equal(1, executor.GetMetrics().IdempotencyWarnings);
    }
}
