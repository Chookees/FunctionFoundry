using FunctionFoundry.Distributed;

namespace FunctionFoundry.Distributed.Tests;

public sealed class QuorumAndPhiExtraTests
{
    [Fact]
    public void Quorum_policy_rejects_invalid_counts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new QuorumPolicy(0, 3).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new QuorumPolicy(2, 0).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new QuorumPolicy(4, 3).Validate());
    }

    [Fact]
    public async Task Quorum_aggregator_rejects_mismatched_task_count()
    {
        var aggregator = new QuorumResultAggregator<int>();
        await Assert.ThrowsAsync<ArgumentException>(() => aggregator.AggregateAsync(
            [System.Threading.Tasks.Task.FromResult(1)],
            new QuorumPolicy(1, 2)));
        await Assert.ThrowsAsync<ArgumentNullException>(() => aggregator.AggregateAsync(null!, new QuorumPolicy(1, 1)));
    }

    [Fact]
    public void Phi_detector_snapshot_before_heartbeats_is_warming_up()
    {
        var clock = new SimulatedClock(10);
        var detector = new PhiAccrualFailureDetector(clock);
        PhiAccrualSnapshot snapshot = detector.GetSnapshot();
        Assert.True(snapshot.IsWarmingUp);
        Assert.Equal(0, snapshot.Phi);
    }
}
