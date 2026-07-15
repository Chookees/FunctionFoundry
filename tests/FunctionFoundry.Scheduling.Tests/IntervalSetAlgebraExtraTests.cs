namespace FunctionFoundry.Scheduling.Tests;

public sealed class IntervalSetAlgebraExtraTests
{
    [Fact]
    public void Union_merges_overlapping_ranges_from_two_sets()
    {
        DateTimeOffset start = new(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);
        TimeInterval early = new(start, start.AddHours(2));
        TimeInterval mid = new(start.AddHours(1), start.AddHours(3));
        TimeInterval late = new(start.AddHours(5), start.AddHours(7));
        IReadOnlyList<TimeInterval> union = IntervalSetAlgebra.Union([early, late], [mid]);
        Assert.Equal(2, union.Count);
        Assert.Equal(start, union[0].Start);
        Assert.Equal(start.AddHours(3), union[0].End);
        Assert.Equal(start.AddHours(5), union[1].Start);
    }

    [Fact]
    public void Difference_removes_covered_ranges()
    {
        DateTimeOffset start = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        TimeInterval baseRange = new(start, start.AddHours(10));
        TimeInterval hole = new(start.AddHours(3), start.AddHours(4));
        IReadOnlyList<TimeInterval> diff = IntervalSetAlgebra.Difference([baseRange], [hole]);
        Assert.Equal(2, diff.Count);
        Assert.Equal(start, diff[0].Start);
        Assert.Equal(start.AddHours(3), diff[0].End);
        Assert.Equal(start.AddHours(4), diff[1].Start);
        Assert.Equal(start.AddHours(10), diff[1].End);
    }

    [Fact]
    public void Normalize_empty_input_returns_empty()
    {
        Assert.Empty(IntervalSetAlgebra.Normalize([]));
    }

    [Fact]
    public void End_before_start_is_empty_interval()
    {
        DateTimeOffset start = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        TimeInterval inverted = new(start.AddHours(2), start);
        Assert.True(inverted.IsEmpty);
        Assert.Empty(IntervalSetAlgebra.Normalize([inverted]));
    }
}
