using FunctionFoundry.Distributed;

namespace FunctionFoundry.Distributed.Tests;

public sealed class VersionClockExtraTests
{
    [Fact]
    public void VersionClockOptions_rejects_non_positive_max_entries()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new VersionClockOptions { MaxNodeEntries = 0 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new VectorClock(new VersionClockOptions { MaxNodeEntries = -1 }));
    }

    [Fact]
    public void Vector_clock_rejects_blank_node_ids_and_invalid_serialization()
    {
        var clock = new VectorClock();
        Assert.Throws<ArgumentException>(() => clock.Increment(" "));
        Assert.Throws<ArgumentException>(() => new VectorClock([new KeyValuePair<string, ulong>("", 1)]));
        Assert.Throws<FormatException>(() => VectorClock.Deserialize("a"));
        Assert.Throws<FormatException>(() => VectorClock.Deserialize("a=x"));
        Assert.Equal(string.Empty, new VectorClock().Serialize());
        Assert.Equal(VersionClockRelation.Equal, VectorClock.Deserialize(string.Empty).CompareTo(new VectorClock()));
    }

    [Fact]
    public void Vector_clock_happens_before_and_equal_paths()
    {
        var ancestor = new VectorClock();
        ancestor.Increment("a");
        var descendant = new VectorClock(ancestor.Entries);
        descendant.Increment("a");
        Assert.Equal(VersionClockRelation.HappensBefore, ancestor.CompareTo(descendant));
        Assert.Equal(VersionClockRelation.HappensAfter, descendant.CompareTo(ancestor));
        Assert.Equal(VersionClockRelation.Equal, ancestor.CompareTo(new VectorClock(ancestor.Entries)));
    }

    [Fact]
    public void Vector_clock_compact_removes_lowest_counters()
    {
        var clock = new VectorClock(new VersionClockOptions { MaxNodeEntries = 3 });
        clock.Increment("a");
        clock.Increment("b");
        clock.Increment("b");
        clock.Increment("c");
        clock.Increment("c");
        clock.Increment("c");
        int removed = clock.Compact();
        Assert.Equal(0, removed);
        clock.Increment("d");
        Assert.Equal(3, clock.Entries.Count);
        Assert.False(clock.Entries.ContainsKey("a"));
    }

    [Fact]
    public void Dotted_clock_rejects_invalid_serialization_and_compacts()
    {
        Assert.Throws<FormatException>(() => DottedVersionClock.Deserialize("bad"));
        Assert.Throws<FormatException>(() => DottedVersionClock.Deserialize("n=x"));
        Assert.Throws<FormatException>(() => DottedVersionClock.Deserialize("n=1:z"));

        var clock = new DottedVersionClock(new VersionClockOptions { MaxNodeEntries = 2 });
        clock.Increment("a");
        clock.Increment("b");
        clock.Increment("c");
        Assert.Equal(2, clock.Entries.Count);
        Assert.Equal(0, clock.Compact());
    }

    [Fact]
    public void Dotted_clock_dominates_false_when_missing_node_or_dot()
    {
        var left = new DottedVersionClock();
        var right = new DottedVersionClock();
        left.Increment("a");
        right.Increment("b");
        Assert.False(left.Dominates(right));

        // Counter advanced without recording the superseded value as a dot.
        var advanced = DottedVersionClock.Deserialize("a=3");
        var earlier = DottedVersionClock.Deserialize("a=2");
        Assert.False(advanced.Dominates(earlier));

        var withDots = DottedVersionClock.Deserialize("a=3:1.2");
        Assert.True(withDots.Dominates(DottedVersionClock.Deserialize("a=1")));
        Assert.True(withDots.Dominates(DottedVersionClock.Deserialize("a=2")));
    }
}
