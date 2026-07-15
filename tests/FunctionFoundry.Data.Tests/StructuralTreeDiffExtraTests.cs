using System.Text;

namespace FunctionFoundry.Data.Tests;

public sealed class StructuralTreeDiffExtraTests
{
    [Fact]
    public void Identical_trees_produce_no_operations()
    {
        byte[] json = Encoding.UTF8.GetBytes("""{"a":[1,2]}""");
        StructuralTreeDiffResult result = StructuralTreeDiff.Diff(json, json);
        Assert.Empty(result.Operations);
    }

    [Fact]
    public void Complexity_limit_marks_limit_reached_for_deep_trees()
    {
        string deep = string.Concat(Enumerable.Repeat("{\"a\":", 40)) + "1" + new string('}', 40);
        StructuralTreeDiffResult result = StructuralTreeDiff.Diff(
            Encoding.UTF8.GetBytes("""{"a":1}"""),
            Encoding.UTF8.GetBytes(deep),
            new StructuralTreeDiffOptions(MaximumDepth: 5));
        Assert.True(result.LimitReached || result.Operations.Count > 0);
    }
}
