using FunctionFoundry.Networking;

namespace FunctionFoundry.Networking.Tests;

public sealed class TransferPlanTests
{
    [Fact]
    public void Create_covers_full_object_without_overlaps()
    {
        TransferPlan plan = TransferPlan.Create(25, 10);
        TransferPlanValidationResult validation = plan.Validate();
        Assert.True(validation.HasFullCoverage);
        Assert.False(validation.HasOverlaps);
        Assert.Equal(3, plan.Chunks.Count);
        Assert.Equal(5, plan.Chunks[^1].Length);
    }

    [Fact]
    public void Incompatible_checkpoint_is_detected()
    {
        TransferPlan plan = TransferPlan.Create(100, 16);
        var checkpoint = new TransferPlanCheckpoint(TransferPlan.CurrentPlanVersion, 90, 16, []);
        Assert.False(plan.IsCheckpointCompatible(checkpoint));
    }

    [Fact]
    public void Repair_returns_only_corrupt_chunks()
    {
        TransferPlan plan = TransferPlan.Create(30, 10);
        IReadOnlyList<TransferChunkAssignment> repair = plan.GetRepairAssignments([1]);
        Assert.Single(repair);
        Assert.Equal(1, repair[0].Index);
    }
}
