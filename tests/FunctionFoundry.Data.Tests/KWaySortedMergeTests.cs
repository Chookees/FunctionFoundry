using FunctionFoundry.Data;

namespace FunctionFoundry.Data.Tests;

public sealed class KWaySortedMergeTests
{
    [Fact]
    public void Merges_sorted_sources()
    {
        IReadOnlyList<IEnumerable<int>> sources =
        [
            [1, 4, 7],
            [2, 5, 8],
            [3, 6, 9],
        ];

        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9], KWaySortedMerge.Merge(sources).ToArray());
    }

    [Fact]
    public void Deduplicates_adjacent_equals()
    {
        IReadOnlyList<IEnumerable<string>> sources =
        [
            ["a", "b", "c"],
            ["b", "c", "d"],
        ];

        Assert.Equal(["a", "b", "c", "d"], KWaySortedMerge.Merge(sources, options: new KWaySortedMergeOptions(true)).ToArray());
    }

    [Fact]
    public void Rejects_null_sources()
    {
        Assert.Throws<ArgumentNullException>(() => KWaySortedMerge.Merge<int>(null!).ToArray());
        Assert.Throws<ArgumentNullException>(() => KWaySortedMerge.Merge<int>([null!]).ToArray());
    }
}
