using FunctionFoundry.Integrity;

namespace FunctionFoundry.Integrity.Tests;

public sealed class MerkleMultiproofTests
{
    [Fact]
    public void Multiproof_verifies_selected_leaves()
    {
        byte[][] leaves =
        [
            "leaf-0"u8.ToArray(),
            "leaf-1"u8.ToArray(),
            "leaf-2"u8.ToArray(),
            "leaf-3"u8.ToArray(),
        ];

        MerkleTree tree = MerkleTree.Build(leaves);
        MerkleMultiproof proof = tree.CreateMultiproof([0, 3]);
        MerkleProofVerificationResult result = MerkleTree.VerifyMultiproof(
            tree.Root.Span,
            [(0, leaves[0]), (3, leaves[3])],
            proof);

        Assert.True(result.IsValid);
        Assert.Equal(tree.Root, result.ComputedRoot);
    }

    [Fact]
    public void Multiproof_rejects_tampered_leaf()
    {
        byte[][] leaves = ["a"u8.ToArray(), "b"u8.ToArray(), "c"u8.ToArray()];
        MerkleTree tree = MerkleTree.Build(leaves);
        MerkleMultiproof proof = tree.CreateMultiproof([1, 2]);
        MerkleProofVerificationResult result = MerkleTree.VerifyMultiproof(
            tree.Root.Span,
            [(1, "tampered"u8.ToArray()), (2, leaves[2])],
            proof);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void CreateMultiproof_rejects_empty_and_out_of_range()
    {
        MerkleTree tree = MerkleTree.Build(["x"u8.ToArray()]);
        Assert.Throws<ArgumentException>(() => tree.CreateMultiproof([]));
        Assert.Throws<ArgumentOutOfRangeException>(() => tree.CreateMultiproof([1]));
    }
}
