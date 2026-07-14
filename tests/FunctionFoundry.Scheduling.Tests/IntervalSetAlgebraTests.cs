namespace FunctionFoundry.Scheduling.Tests;

public sealed class IntervalSetAlgebraTests
{
    [Fact]
    public void Normalize_merges_overlapping_closed_intervals()
    {
        DateTimeOffset start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        TimeInterval[] input =
        [
            new(start, start.AddHours(3)),
            new(start.AddHours(2), start.AddHours(5)),
        ];
        IReadOnlyList<TimeInterval> normalized = IntervalSetAlgebra.Normalize(input);
        Assert.Single(normalized);
        Assert.Equal(start, normalized[0].Start);
        Assert.Equal(start.AddHours(5), normalized[0].End);
    }

    [Fact]
    public void Intersection_difference_and_complement_compose()
    {
        DateTimeOffset start = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
        TimeInterval universe = new(start, start.AddHours(10));
        TimeInterval a = new(start, start.AddHours(6));
        TimeInterval b = new(start.AddHours(4), start.AddHours(8));
        IReadOnlyList<TimeInterval> intersection = IntervalSetAlgebra.Intersection([a], [b]);
        Assert.Single(intersection);
        Assert.Equal(start.AddHours(4), intersection[0].Start);
        IReadOnlyList<TimeInterval> complement = IntervalSetAlgebra.Complement(intersection, universe);
        Assert.Equal(2, complement.Count);
    }

    [Fact]
    public void Open_and_closed_endpoints_affect_overlap()
    {
        DateTimeOffset t = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        TimeInterval left = new(t, t, IntervalEndpoint.Closed, IntervalEndpoint.Open);
        TimeInterval right = new(t, t.AddHours(1));
        IReadOnlyList<TimeInterval> intersection = IntervalSetAlgebra.Intersection([left], [right]);
        Assert.Empty(intersection);
    }
}
