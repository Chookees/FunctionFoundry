namespace FunctionFoundry.Distributed.Tests;

public sealed class VersionClockTests
{
    [Fact]
    public void Vector_clock_increment_merge_and_compare()
    {
        var left = new VectorClock();
        var right = new VectorClock();
        left.Increment("a");
        right.Increment("b");
        Assert.Equal(VersionClockRelation.Concurrent, left.CompareTo(right));

        right.Merge(left);
        left.Merge(right);
        left.Increment("a");
        Assert.Equal(VersionClockRelation.HappensAfter, left.CompareTo(right));
    }

    [Fact]
    public void Vector_clock_serialization_is_deterministic()
    {
        var clock = new VectorClock();
        clock.Increment("b");
        clock.Increment("a");
        clock.Increment("a");
        string serialized = clock.Serialize();
        Assert.Equal("a=2,b=1", serialized);
        VectorClock parsed = VectorClock.Deserialize(serialized);
        Assert.Equal(VersionClockRelation.Equal, clock.CompareTo(parsed));
    }

    [Fact]
    public void Vector_clock_compacts_when_growth_limit_exceeded()
    {
        var clock = new VectorClock(new VersionClockOptions { MaxNodeEntries = 2 });
        clock.Increment("a");
        clock.Increment("b");
        clock.Increment("c");
        Assert.Equal(2, clock.Entries.Count);
    }

    [Fact]
    public void Dotted_clock_merge_and_dominance()
    {
        var left = new DottedVersionClock();
        var right = new DottedVersionClock();
        left.Increment("n");
        right.Increment("n");
        right.Merge(left);
        left.Increment("n");
        Assert.False(right.Dominates(left));
        right.Merge(left);
        Assert.True(right.Dominates(left));
    }

    [Fact]
    public void Dotted_clock_serialization_round_trip()
    {
        var clock = new DottedVersionClock();
        clock.Increment("x");
        clock.Increment("x");
        string serialized = clock.Serialize();
        DottedVersionClock parsed = DottedVersionClock.Deserialize(serialized);
        Assert.Equal(serialized, parsed.Serialize());
    }
}
