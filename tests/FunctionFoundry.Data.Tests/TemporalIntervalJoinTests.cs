namespace FunctionFoundry.Data.Tests;

public sealed class TemporalIntervalJoinTests
{
    [Fact]
    public void Overlapping_intervals_join()
    {
        var left = new[]
        {
            new TemporalRecord<int, string>(1, new TemporalInterval<int>(0, 10), "L"),
        };
        var right = new[]
        {
            new TemporalRecord<int, string>(1, new TemporalInterval<int>(5, 15), "R"),
        };

        TemporalIntervalJoinResult<int, string, string> result = TemporalIntervalJoin.Join(left, right);
        Assert.Single(result.Matches);
        Assert.Equal(5, result.Matches[0].Overlap.Start);
        Assert.Equal(10, result.Matches[0].Overlap.End);
        Assert.Empty(result.InvalidLeft);
        Assert.Empty(result.InvalidRight);
    }

    [Fact]
    public void Invalid_intervals_are_reported()
    {
        var left = new[]
        {
            new TemporalRecord<int, string>(1, new TemporalInterval<int>(10, 0), "bad"),
        };
        TemporalIntervalJoinResult<int, string, string> result = TemporalIntervalJoin.Join(left, Array.Empty<TemporalRecord<int, string>>());
        Assert.Single(result.InvalidLeft);
        Assert.Equal(InvalidIntervalReason.EndBeforeStart, result.InvalidLeft[0].Reason);
    }

    [Fact]
    public void Open_endpoints_can_yield_empty_overlap()
    {
        var left = new TemporalInterval<int>(0, 5, IntervalEndpointKind.Closed, IntervalEndpointKind.Open);
        var right = new TemporalInterval<int>(5, 10, IntervalEndpointKind.Closed, IntervalEndpointKind.Closed);
        Assert.False(TemporalIntervalJoin.TryOverlap(left, right, out _));
    }
}
