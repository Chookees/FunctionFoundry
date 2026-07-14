using FunctionFoundry.Resilience;

namespace FunctionFoundry.Resilience.Tests;

public sealed class CheckpointedBatchExecutorTests
{
    private static readonly string[] ResumeItems = ["a", "b", "c"];
    private static readonly string[] PartialFailureItems = ["ok", "bad", "ok2"];
    private static readonly string[] IdempotencyItems = ["x"];

    [Fact]
    public async Task Resume_skips_completed_items()
    {
        var store = new InMemoryCheckpointStore();
        var executor = new CheckpointedBatchExecutor(store, new CheckpointedBatchExecutorOptions { MaxConcurrency = 2 });
        await store.SaveAsync(new BatchCheckpointState("run-1", ["a"], [], DateTimeOffset.UtcNow));

        List<string> processed = [];
        BatchExecutionResult result = await executor.ExecuteAsync(
            "run-1",
            ResumeItems,
            static item => item,
            (item, _) =>
            {
                processed.Add(item);
                return Task.CompletedTask;
            },
            isIdempotent: true);

        Assert.True(result.ResumedFromCheckpoint);
        Assert.Equal(["b", "c"], processed);
        Assert.Equal(["a"], result.SkippedAsCompleted);
        Assert.Equal(2, result.Completed.Count);
    }

    [Fact]
    public async Task Partial_failures_are_recorded_without_hidden_retries()
    {
        var store = new InMemoryCheckpointStore();
        var executor = new CheckpointedBatchExecutor(store);
        BatchExecutionResult result = await executor.ExecuteAsync(
            "run-2",
            PartialFailureItems,
            static item => item,
            (item, _) => item == "bad" ? throw new InvalidOperationException("fail") : Task.CompletedTask,
            isIdempotent: true);

        Assert.Single(result.Failed);
        Assert.Equal(2, result.Completed.Count);
        BatchCheckpointState? checkpoint = await store.LoadAsync("run-2");
        Assert.NotNull(checkpoint);
        Assert.Contains("bad", checkpoint.FailedItemIds);
    }

    [Fact]
    public async Task Requires_explicit_idempotency_when_configured()
    {
        var executor = new CheckpointedBatchExecutor(new InMemoryCheckpointStore());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync(
                "run-3",
                IdempotencyItems,
                static item => item,
                (_, _) => Task.CompletedTask,
                isIdempotent: false));
    }
}
