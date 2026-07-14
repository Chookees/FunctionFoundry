namespace FunctionFoundry.Distributed.Tests;

public sealed class QuorumResultAggregatorTests
{
    [Fact]
    public async Task Succeeds_early_when_quorum_agrees()
    {
        var aggregator = new QuorumResultAggregator<string>();
        Task<string>[] tasks =
        [
            System.Threading.Tasks.Task.FromResult("v1"),
            System.Threading.Tasks.Task.FromResult("v1"),
            SlowResult("v2"),
        ];
        QuorumResult<string> result = await aggregator.AggregateAsync(tasks, new QuorumPolicy(2, 3));
        Assert.Equal(QuorumStatus.Succeeded, result.Status);
        Assert.Equal("v1", result.Value);
        Assert.Equal(2, result.SuccessCount);
    }

    [Fact]
    public async Task Reports_conflict_when_values_disagree()
    {
        var aggregator = new QuorumResultAggregator<int>();
        Task<int>[] tasks =
        [
            System.Threading.Tasks.Task.FromResult(1),
            System.Threading.Tasks.Task.FromResult(2),
            System.Threading.Tasks.Task.FromResult(3),
        ];
        QuorumResult<int> result = await aggregator.AggregateAsync(tasks, new QuorumPolicy(2, 3));
        Assert.Equal(QuorumStatus.Conflict, result.Status);
    }

    [Fact]
    public async Task Deterministic_tie_break_when_policy_allows()
    {
        var aggregator = new QuorumResultAggregator<string>();
        Task<string>[] tasks =
        [
            System.Threading.Tasks.Task.FromResult("alpha"),
            System.Threading.Tasks.Task.FromResult("beta"),
            System.Threading.Tasks.Task.FromResult("alpha"),
        ];
        QuorumResult<string> result = await aggregator.AggregateAsync(
            tasks,
            new QuorumPolicy(2, 3, AllowDeterministicTieBreak: true));

        Assert.Equal(QuorumStatus.Succeeded, result.Status);
        Assert.Equal("alpha", result.Value);
        Assert.False(result.WasDeterministicSelectionApplied);
    }

    [Fact]
    public async Task Fails_with_evidence_when_too_many_errors()
    {
        var aggregator = new QuorumResultAggregator<string>();
        Task<string>[] tasks =
        [
            System.Threading.Tasks.Task.FromException<string>(new InvalidOperationException("boom")),
            System.Threading.Tasks.Task.FromException<string>(new InvalidOperationException("boom2")),
            System.Threading.Tasks.Task.FromResult("ok"),
        ];
        QuorumResult<string> result = await aggregator.AggregateAsync(tasks, new QuorumPolicy(2, 3));
        Assert.Equal(QuorumStatus.Failed, result.Status);
        Assert.Equal(2, result.FailureEvidence.Count);
    }

    private static System.Threading.Tasks.Task<T> SlowResult<T>(T value)
    {
        var tcs = new System.Threading.Tasks.TaskCompletionSource<T>();
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            await System.Threading.Tasks.Task.Delay(2_000).ConfigureAwait(false);
            tcs.SetResult(value);
        });
        return tcs.Task;
    }
}
