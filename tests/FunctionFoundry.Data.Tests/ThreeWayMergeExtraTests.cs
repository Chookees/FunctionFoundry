using System.Text;

namespace FunctionFoundry.Data.Tests;

public sealed class ThreeWayMergeExtraTests
{
    [Fact]
    public void Identical_inputs_return_base_without_conflicts()
    {
        byte[] json = Encoding.UTF8.GetBytes("""{"x":1}""");
        ThreeWayMergeResult result = ThreeWayMerge.Merge(json, json, json);
        Assert.False(result.HasConflicts);
        Assert.NotNull(result.Merged);
    }

    [Fact]
    public void Invalid_json_throws()
    {
        Assert.ThrowsAny<Exception>(() => ThreeWayMerge.Merge("{}"u8.ToArray(), "not-json"u8.ToArray(), "{}"u8.ToArray()));
    }

    [Fact]
    public void Nested_non_conflicting_paths_merge()
    {
        ThreeWayMergeResult result = ThreeWayMerge.Merge(
            Encoding.UTF8.GetBytes("""{"o":{"a":1,"b":1}}"""),
            Encoding.UTF8.GetBytes("""{"o":{"a":2,"b":1}}"""),
            Encoding.UTF8.GetBytes("""{"o":{"a":1,"b":3}}"""));
        Assert.False(result.HasConflicts);
        Assert.Equal(2, result.Merged!.Value.GetProperty("o").GetProperty("a").GetInt32());
        Assert.Equal(3, result.Merged.Value.GetProperty("o").GetProperty("b").GetInt32());
    }
}
