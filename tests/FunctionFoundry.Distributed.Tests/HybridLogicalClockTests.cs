using FunctionFoundry.Distributed;

namespace FunctionFoundry.Distributed.Tests;

public sealed class HybridLogicalClockTests
{
    [Fact]
    public void Now_advances_logical_counter_when_physical_unchanged()
    {
        var clock = new SimulatedClock(1_000);
        var hlc = new HybridLogicalClock(clock);
        HybridLogicalTimestamp first = hlc.Now();
        HybridLogicalTimestamp second = hlc.Now();
        Assert.Equal(1_000, first.PhysicalTimeMs);
        Assert.Equal(0, first.LogicalCounter);
        Assert.Equal(1_000, second.PhysicalTimeMs);
        Assert.Equal(1, second.LogicalCounter);
    }

    [Fact]
    public void Receive_merges_remote_causality()
    {
        var clock = new SimulatedClock(5_000);
        var hlc = new HybridLogicalClock(clock);
        HybridLogicalTimestamp remote = new(8_000, 3);
        HybridLogicalTimestamp received = hlc.Receive(remote);
        Assert.Equal(8_000, received.PhysicalTimeMs);
        Assert.Equal(4, received.LogicalCounter);
    }

    [Fact]
    public void Serialization_round_trips_and_compare_orders()
    {
        HybridLogicalTimestamp left = new(10, 1);
        HybridLogicalTimestamp right = new(10, 2);
        Assert.True(left.CompareTo(right) < 0);
        Assert.Equal(left, HybridLogicalTimestamp.Deserialize(left.Serialize()));
        Assert.Throws<FormatException>(() => HybridLogicalTimestamp.Deserialize("bad"));
    }
}
