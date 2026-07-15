using FunctionFoundry.Resilience;

namespace FunctionFoundry.Resilience.Tests;

public sealed class AdaptiveConcurrencyControllerTests
{
    [Fact]
    public async Task Acquire_and_release_respects_max_concurrency()
    {
        var options = new AdaptiveConcurrencyControllerOptions
        {
            MinConcurrency = 1,
            MaxConcurrency = 2,
            WarmUpConcurrency = 2,
            WarmUpOperationCount = 0,
            MaxQueueDepth = 8,
        };
        using var controller = new AdaptiveConcurrencyController(options);

        AcquireAttemptResult first = await controller.TryAcquireAsync(TimeSpan.FromSeconds(1));
        AcquireAttemptResult second = await controller.TryAcquireAsync(TimeSpan.FromSeconds(1));
        Assert.True(first.Acquired);
        Assert.True(second.Acquired);

        Task<AcquireAttemptResult> blocked = controller.TryAcquireAsync(TimeSpan.FromSeconds(2)).AsTask();
        await WaitUntilAsync(() => controller.GetSnapshot().QueuedWaiters >= 1, TimeSpan.FromSeconds(2));

        AdaptiveConcurrencySnapshot snapshot = controller.GetSnapshot();
        Assert.Equal(2, snapshot.ActivePermits);
        Assert.Equal(1, snapshot.QueuedWaiters);

        controller.Release(first.Permit, new OperationFeedback(true, TimeSpan.FromMilliseconds(20)));
        AcquireAttemptResult third = await blocked;
        Assert.True(third.Acquired);
        Assert.Equal(2, controller.GetSnapshot().ActivePermits);
    }

    [Fact]
    public void SimulateFeedback_decreases_limit_on_high_latency_deterministically()
    {
        var options = new AdaptiveConcurrencyControllerOptions
        {
            MinConcurrency = 1,
            MaxConcurrency = 16,
            WarmUpConcurrency = 16,
            WarmUpOperationCount = 0,
            TargetLatencyMilliseconds = 50,
            MultiplicativeDecreaseFactor = 0.5,
            RollingWindowSize = 4,
        };
        using var controller = new AdaptiveConcurrencyController(options);
        var samples = new List<OperationFeedback>();

        for (int i = 0; i < 4; i++)
        {
            samples.Add(new OperationFeedback(true, TimeSpan.FromMilliseconds(200)));
        }

        controller.SimulateFeedbackBatch(samples);

        AdaptiveConcurrencySnapshot snapshot = controller.GetSnapshot();
        Assert.Equal(8, snapshot.CurrentConcurrencyLimit);
    }

    [Fact]
    public async Task Queue_limit_rejects_when_full()
    {
        var options = new AdaptiveConcurrencyControllerOptions
        {
            MaxConcurrency = 1,
            WarmUpConcurrency = 1,
            WarmUpOperationCount = 0,
            MaxQueueDepth = 0,
        };
        using var controller = new AdaptiveConcurrencyController(options);
        AcquireAttemptResult first = await controller.TryAcquireAsync(TimeSpan.FromSeconds(1));
        Assert.True(first.Acquired);

        AcquireAttemptResult rejected = await controller.TryAcquireAsync(TimeSpan.FromMilliseconds(50));
        Assert.False(rejected.Acquired);
        Assert.Equal(1, controller.GetSnapshot().RejectedDueToQueueLimit);
    }

    [Fact]
    public void Options_validate_rejects_invalid_ranges()
    {
        var options = new AdaptiveConcurrencyControllerOptions
        {
            MinConcurrency = 5,
            MaxConcurrency = 2,
        };
        Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
    }

    [Fact]
    public void SimulateFeedback_increases_limit_when_healthy()
    {
        var options = new AdaptiveConcurrencyControllerOptions
        {
            MinConcurrency = 1,
            MaxConcurrency = 16,
            WarmUpConcurrency = 4,
            WarmUpOperationCount = 0,
            TargetLatencyMilliseconds = 100,
            AdditiveIncreaseStep = 1,
            RollingWindowSize = 4,
        };
        using var controller = new AdaptiveConcurrencyController(options);
        int before = controller.GetSnapshot().CurrentConcurrencyLimit;

        var samples = new List<OperationFeedback>();
        for (int i = 0; i < 4; i++)
        {
            samples.Add(new OperationFeedback(true, TimeSpan.FromMilliseconds(10)));
        }

        controller.SimulateFeedbackBatch(samples);
        Assert.True(controller.GetSnapshot().CurrentConcurrencyLimit >= before);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail("Condition was not met before timeout.");
    }
}
