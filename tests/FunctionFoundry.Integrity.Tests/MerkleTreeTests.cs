using System.Security.Cryptography;

namespace FunctionFoundry.Integrity.Tests;

public sealed class MerkleTreeTests
{
    [Fact]
    public void Inclusion_proof_verifies_for_single_and_multiple_leaves()
    {
        byte[][] leaves =
        [
            "leaf-0"u8.ToArray(),
            "leaf-1"u8.ToArray(),
            "leaf-2"u8.ToArray(),
        ];

        MerkleTree tree = MerkleTree.Build(leaves);
        MerkleProof proof = tree.CreateProof(1);
        MerkleProofVerificationResult ok = MerkleTree.VerifyProof(tree.Root.Span, leaves[1], proof);
        Assert.True(ok.IsValid);
        Assert.Equal(tree.Root, ok.ComputedRoot);
    }

    [Fact]
    public void Odd_leaf_count_duplicates_last_node_deterministically()
    {
        byte[][] three =
        [
            [1],
            [2],
            [3],
        ];

        MerkleTree first = MerkleTree.Build(three);
        MerkleTree second = MerkleTree.Build(three);
        Assert.Equal(first.Root, second.Root);

        MerkleProof proof = first.CreateProof(2);
        MerkleProofVerificationResult result = MerkleTree.VerifyProof(first.Root.Span, three[2], proof);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Tampered_leaf_fails_proof_verification()
    {
        byte[][] leaves = ["a"u8.ToArray(), "b"u8.ToArray()];
        MerkleTree tree = MerkleTree.Build(leaves);
        MerkleProof proof = tree.CreateProof(0);
        MerkleProofVerificationResult result = MerkleTree.VerifyProof(tree.Root.Span, "tampered"u8.ToArray(), proof);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Domain_separation_changes_root_when_prefix_differs()
    {
        byte[] leaf = "payload"u8.ToArray();
        MerkleTree tree = MerkleTree.Build([leaf]);

        byte[] rawLeafHash = SHA256.HashData(leaf);
        Assert.False(rawLeafHash.AsSpan().SequenceEqual(tree.Root.Span));
    }

    [Fact]
    public void Sha384_option_is_supported()
    {
        MerkleTree tree = MerkleTree.Build(["x"u8.ToArray()], new MerkleTreeOptions(ManifestHashAlgorithm.Sha384));
        Assert.Equal(48, tree.Root.Length);
    }

    [Fact]
    public void CreateProof_throws_for_out_of_range_index()
    {
        MerkleTree tree = MerkleTree.Build(["leaf"u8.ToArray()]);
        Assert.Throws<ArgumentOutOfRangeException>(() => tree.CreateProof(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => tree.CreateProof(-1));
    }
}
