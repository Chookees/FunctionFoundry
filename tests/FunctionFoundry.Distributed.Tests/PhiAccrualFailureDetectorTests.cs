namespace FunctionFoundry.Distributed.Tests;

public sealed class PhiAccrualFailureDetectorTests
{
    [Fact]
    public void Warmup_keeps_phi_at_zero()
    {
        var clock = new SimulatedClock(0);
        var detector = new PhiAccrualFailureDetector(clock, new PhiAccrualFailureDetectorOptions { WarmupIntervals = 4 });
        detector.RecordHeartbeatAt(0);
        detector.RecordHeartbeatAt(1_000);
        PhiAccrualSnapshot snapshot = detector.GetSnapshot();
        Assert.True(snapshot.IsWarmingUp);
        Assert.Equal(0, snapshot.Phi);
    }

    [Fact]
    public void Deterministic_simulation_raises_phi_after_pause()
    {
        var clock = new SimulatedClock(0);
        var detector = new PhiAccrualFailureDetector(clock, new PhiAccrualFailureDetectorOptions { WarmupIntervals = 3, PhiThreshold = 3 });
        long[] beats = [0, 1_000, 2_000, 3_000, 4_000, 5_000, 6_000];
        foreach (long beat in beats)
        {
            detector.RecordHeartbeatAt(beat);
        }

        clock.Advance(20_000);
        PhiAccrualSnapshot snapshot = detector.GetSnapshot();
        Assert.False(snapshot.IsWarmingUp);
        Assert.True(snapshot.Phi > 3);
        Assert.False(snapshot.IsAvailable);
    }

    [Fact]
    public void Outlier_interval_does_not_prevent_detection_after_long_pause()
    {
        var clock = new SimulatedClock(0);
        var detector = new PhiAccrualFailureDetector(clock, new PhiAccrualFailureDetectorOptions { WarmupIntervals = 2 });
        detector.RecordHeartbeatAt(0);
        detector.RecordHeartbeatAt(1_000);
        detector.RecordHeartbeatAt(2_000);
        detector.RecordHeartbeatAt(50_000);
        detector.RecordHeartbeatAt(51_000);
        clock.Advance(70_000);
        Assert.True(detector.GetSnapshot().Phi > 0);
    }
}
