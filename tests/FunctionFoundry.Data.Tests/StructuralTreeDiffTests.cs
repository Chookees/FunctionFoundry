using System.Text;
using System.Text.Json;

namespace FunctionFoundry.Data.Tests;

public sealed class StructuralTreeDiffTests
{
    [Fact]
    public void Detects_add_remove_and_replace()
    {
        string left = """{"name":"alice","count":1}""";
        string right = """{"name":"alice","count":2,"active":true}""";
        StructuralTreeDiffResult result = StructuralTreeDiff.Diff(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));

        Assert.Contains(result.Operations, op => op.Kind == StructuralDiffOperationKind.Replace && op.Path == "/count");
        Assert.Contains(result.Operations, op => op.Kind == StructuralDiffOperationKind.Add && op.Path == "/active");
        Assert.False(result.LimitReached);
        Assert.NotEmpty(result.ToCanonicalUtf8Json());
        Assert.Contains("count", result.ToHumanReadable(), StringComparison.Ordinal);
    }

    [Fact]
    public void Identity_arrays_emit_move_operations()
    {
        string left = """{"items":[{"id":"a","v":1},{"id":"b","v":2}]}""";
        string right = """{"items":[{"id":"b","v":2},{"id":"a","v":1}]}""";
        StructuralTreeDiffResult result = StructuralTreeDiff.Diff(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));

        Assert.Contains(result.Operations, op => op.Kind == StructuralDiffOperationKind.Move);
    }

    [Fact]
    public void Complexity_limit_sets_flag()
    {
        string json = """{"a":1}""";
        StructuralTreeDiffResult result = StructuralTreeDiff.Diff(
            Encoding.UTF8.GetBytes(json),
            Encoding.UTF8.GetBytes(json),
            new StructuralTreeDiffOptions(MaximumNodeVisits: 1));

        Assert.True(result.LimitReached);
    }
}
