using System.Text;
using System.Text.Json;

namespace FunctionFoundry.Data.Tests;

public sealed class ThreeWayMergeTests
{
    [Fact]
    public void Non_conflicting_changes_merge()
    {
        string baseJson = """{"a":1,"b":2}""";
        string localJson = """{"a":1,"b":3}""";
        string remoteJson = """{"a":2,"b":2}""";
        ThreeWayMergeResult result = ThreeWayMerge.Merge(
            Encoding.UTF8.GetBytes(baseJson),
            Encoding.UTF8.GetBytes(localJson),
            Encoding.UTF8.GetBytes(remoteJson));

        Assert.False(result.HasConflicts);
        Assert.NotNull(result.Merged);
        JsonElement merged = result.Merged!.Value;
        Assert.Equal(3, merged.GetProperty("b").GetInt32());
        Assert.Equal(2, merged.GetProperty("a").GetInt32());
    }

    [Fact]
    public void Concurrent_edits_surface_conflicts()
    {
        string baseJson = """{"a":1}""";
        string localJson = """{"a":2}""";
        string remoteJson = """{"a":3}""";
        ThreeWayMergeResult result = ThreeWayMerge.Merge(
            Encoding.UTF8.GetBytes(baseJson),
            Encoding.UTF8.GetBytes(localJson),
            Encoding.UTF8.GetBytes(remoteJson));

        Assert.True(result.HasConflicts);
        Assert.Null(result.Merged);
        Assert.Single(result.Conflicts);
        Assert.Equal(ThreeWayMergeConflictKind.ConcurrentEdit, result.Conflicts[0].Kind);
        Assert.Contains("ConcurrentEdit", result.ToHumanReadable(), StringComparison.Ordinal);
    }

    [Fact]
    public void Delete_modify_conflict_is_explicit()
    {
        string baseJson = """{"a":1}""";
        string localJson = """{}""";
        string remoteJson = """{"a":2}""";
        ThreeWayMergeResult result = ThreeWayMerge.Merge(
            Encoding.UTF8.GetBytes(baseJson),
            Encoding.UTF8.GetBytes(localJson),
            Encoding.UTF8.GetBytes(remoteJson));

        Assert.True(result.HasConflicts);
        Assert.Equal(ThreeWayMergeConflictKind.DeleteModify, result.Conflicts[0].Kind);
    }
}
