using System.Security.Cryptography;

namespace FunctionFoundry.Integrity;

/// <summary>
/// Options for <see cref="MerkleTree"/> construction and proof verification.
/// </summary>
/// <param name="HashAlgorithm">Digest algorithm used for tree nodes. Defaults to SHA-256.</param>
public sealed record MerkleTreeOptions(ManifestHashAlgorithm HashAlgorithm = ManifestHashAlgorithm.Sha256);

/// <summary>
/// Inclusion proof for a single leaf in a <see cref="MerkleTree"/>.
/// </summary>
/// <param name="LeafIndex">Zero-based leaf index.</param>
/// <param name="Siblings">Sibling hashes from leaf level to root, excluding the root.</param>
/// <param name="LeafCount">Total leaf count in the original tree.</param>
public sealed record MerkleProof(int LeafIndex, IReadOnlyList<byte[]> Siblings, int LeafCount);

/// <summary>
/// Compact multi-leaf inclusion proof for a <see cref="MerkleTree"/>.
/// </summary>
/// <param name="LeafIndices">Sorted unique leaf indices covered by the proof.</param>
/// <param name="Nodes">Helper node hashes required to reconstruct the root.</param>
/// <param name="LeafCount">Total leaf count in the original tree.</param>
public sealed record MerkleMultiproof(IReadOnlyList<int> LeafIndices, IReadOnlyList<byte[]> Nodes, int LeafCount);

/// <summary>
/// Result of Merkle proof verification.
/// </summary>
/// <param name="IsValid">Whether the proof matches the supplied root for the leaf.</param>
/// <param name="ComputedRoot">Root recomputed from the leaf and proof.</param>
public sealed record MerkleProofVerificationResult(bool IsValid, ReadOnlyMemory<byte> ComputedRoot);

/// <summary>
/// Builds deterministic Merkle trees, creates inclusion proofs, and verifies them.
/// </summary>
/// <remarks>
/// <para>Leaf encoding: <c>0x00 || leafBytes</c>.</para>
/// <para>Internal node encoding: <c>0x01 || leftDigest || rightDigest</c> where digests are raw bytes ordered left-to-right.</para>
/// <para>Odd-node policy: when a level has an odd number of nodes, the final node is duplicated before pairing.</para>
/// <para>Thread safety: static methods are thread-safe; instances are immutable after construction.</para>
/// </remarks>
public sealed class MerkleTree
{
    private static readonly byte[] LeafDomainPrefix = [0x00];
    private static readonly byte[] InternalDomainPrefix = [0x01];

    private readonly MerkleTreeOptions _options;
    private readonly byte[][] _level0;
    private readonly byte[] _root;

    private MerkleTree(MerkleTreeOptions options, byte[][] level0, byte[] root)
    {
        _options = options;
        _level0 = level0;
        _root = root;
    }

    /// <summary>
    /// Gets the Merkle root digest.
    /// </summary>
    public ReadOnlyMemory<byte> Root => _root;

    /// <summary>
    /// Gets the number of leaves in the tree.
    /// </summary>
    public int LeafCount => _level0.Length;

    /// <summary>
    /// Builds a Merkle tree from leaf payloads.
    /// </summary>
    /// <param name="leaves">Ordered leaf payloads. Must contain at least one leaf.</param>
    /// <param name="options">Optional tree options.</param>
    /// <returns>Immutable Merkle tree.</returns>
    /// <exception cref="ArgumentException">Thrown when no leaves are supplied.</exception>
    public static MerkleTree Build(IReadOnlyList<byte[]> leaves, MerkleTreeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(leaves);
        if (leaves.Count == 0)
        {
            throw new ArgumentException("At least one leaf is required.", nameof(leaves));
        }

        MerkleTreeOptions resolved = options ?? new MerkleTreeOptions();
        byte[][] level0 = leaves.Select(leaf => HashLeaf(leaf, resolved.HashAlgorithm)).ToArray();
        byte[] root = BuildRoot(level0, resolved.HashAlgorithm);
        return new MerkleTree(resolved, level0, root);
    }

    /// <summary>
    /// Creates an inclusion proof for the leaf at <paramref name="leafIndex"/>.
    /// </summary>
    /// <param name="leafIndex">Zero-based leaf index.</param>
    /// <returns>Inclusion proof.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the index is out of range.</exception>
    public MerkleProof CreateProof(int leafIndex)
    {
        if (leafIndex < 0 || leafIndex >= _level0.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(leafIndex));
        }

        var siblings = new List<byte[]>();
        byte[][] current = _level0;
        int index = leafIndex;

        while (current.Length > 1)
        {
            int pairIndex = index ^ 1;
            if (pairIndex >= current.Length)
            {
                pairIndex = index;
            }

            siblings.Add((byte[])current[pairIndex].Clone());
            index /= 2;
            current = BuildNextLevel(current, _options.HashAlgorithm);
        }

        return new MerkleProof(leafIndex, siblings, _level0.Length);
    }

    /// <summary>
    /// Creates a multiproof covering the supplied leaf indices.
    /// </summary>
    /// <param name="leafIndices">Leaf indices to prove. Duplicates are ignored.</param>
    /// <returns>Multiproof with deterministic helper node ordering.</returns>
    public MerkleMultiproof CreateMultiproof(IEnumerable<int> leafIndices)
    {
        ArgumentNullException.ThrowIfNull(leafIndices);
        int[] indices = leafIndices.Distinct().OrderBy(static i => i).ToArray();
        if (indices.Length == 0)
        {
            throw new ArgumentException("At least one leaf index is required.", nameof(leafIndices));
        }

        foreach (int index in indices)
        {
            if (index < 0 || index >= _level0.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(leafIndices), $"Leaf index {index} is out of range.");
            }
        }

        // Correctness-first multiproof: concatenate single-proof siblings in index order.
        // Shared nodes may repeat; verification reconstructs each leaf path independently then checks root equality.
        var nodes = new List<byte[]>();
        foreach (int index in indices)
        {
            MerkleProof single = CreateProof(index);
            foreach (byte[] sibling in single.Siblings)
            {
                nodes.Add((byte[])sibling.Clone());
            }
        }

        return new MerkleMultiproof(indices, nodes, _level0.Length);
    }

    /// <summary>
    /// Verifies a multiproof for multiple leaf payloads against an expected root.
    /// </summary>
    /// <param name="expectedRoot">Expected Merkle root digest.</param>
    /// <param name="leaves">Leaf payloads keyed by index; must match <see cref="MerkleMultiproof.LeafIndices"/>.</param>
    /// <param name="proof">Multiproof.</param>
    /// <param name="options">Optional tree options.</param>
    public static MerkleProofVerificationResult VerifyMultiproof(
        ReadOnlySpan<byte> expectedRoot,
        IReadOnlyList<(int LeafIndex, byte[] Leaf)> leaves,
        MerkleMultiproof proof,
        MerkleTreeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(leaves);
        ArgumentNullException.ThrowIfNull(proof);
        if (leaves.Count != proof.LeafIndices.Count)
        {
            throw new ArgumentException("Leaf payload count must match multiproof leaf indices.", nameof(leaves));
        }

        int nodeOffset = 0;
        byte[]? firstRoot = null;
        for (int i = 0; i < proof.LeafIndices.Count; i++)
        {
            int expectedIndex = proof.LeafIndices[i];
            (int leafIndex, byte[] leaf) = leaves[i];
            if (leafIndex != expectedIndex)
            {
                throw new ArgumentException($"Leaf index mismatch at position {i}.", nameof(leaves));
            }

            ArgumentNullException.ThrowIfNull(leaf);
            int remainingLevels = CountProofLevels(proof.LeafCount);
            if (nodeOffset + remainingLevels > proof.Nodes.Count)
            {
                throw new ArgumentException("Multiproof does not contain enough helper nodes.", nameof(proof));
            }

            byte[][] siblings = new byte[remainingLevels][];
            for (int level = 0; level < remainingLevels; level++)
            {
                siblings[level] = proof.Nodes[nodeOffset++];
            }

            MerkleProof single = new(leafIndex, siblings, proof.LeafCount);
            MerkleProofVerificationResult result = VerifyProof(expectedRoot, leaf, single, options);
            if (!result.IsValid)
            {
                return result;
            }

            firstRoot ??= result.ComputedRoot.ToArray();
            if (!firstRoot.AsSpan().SequenceEqual(result.ComputedRoot.Span))
            {
                return new MerkleProofVerificationResult(false, result.ComputedRoot);
            }
        }

        if (nodeOffset != proof.Nodes.Count)
        {
            throw new ArgumentException("Multiproof contains unused helper nodes.", nameof(proof));
        }

        return new MerkleProofVerificationResult(true, firstRoot ?? expectedRoot.ToArray());
    }

    private static int CountProofLevels(int leafCount)
    {
        int levels = 0;
        int size = leafCount;
        while (size > 1)
        {
            levels++;
            size = (size + 1) / 2;
        }

        return levels;
    }

    /// <summary>
    /// Verifies an inclusion proof for a leaf payload against an expected root.
    /// </summary>
    /// <param name="expectedRoot">Expected Merkle root digest.</param>
    /// <param name="leaf">Leaf payload bytes.</param>
    /// <param name="proof">Inclusion proof.</param>
    /// <param name="options">Optional tree options.</param>
    /// <returns>Verification result containing validity and recomputed root.</returns>
    public static MerkleProofVerificationResult VerifyProof(
        ReadOnlySpan<byte> expectedRoot,
        ReadOnlySpan<byte> leaf,
        MerkleProof proof,
        MerkleTreeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(proof);
        if (proof.LeafIndex < 0 || proof.LeafIndex >= proof.LeafCount)
        {
            throw new ArgumentOutOfRangeException(nameof(proof), "Leaf index is out of range for proof.");
        }

        MerkleTreeOptions resolved = options ?? new MerkleTreeOptions();
        byte[] computed = HashLeaf(leaf, resolved.HashAlgorithm);
        int index = proof.LeafIndex;
        int levelSize = proof.LeafCount;

        foreach (byte[] sibling in proof.Siblings)
        {
            bool indexIsRight = (index & 1) == 1;
            computed = indexIsRight
                ? HashInternal(sibling, computed, resolved.HashAlgorithm)
                : HashInternal(computed, sibling, resolved.HashAlgorithm);

            index /= 2;
            levelSize = (levelSize + 1) / 2;
        }

        bool valid = computed.AsSpan().SequenceEqual(expectedRoot);
        return new MerkleProofVerificationResult(valid, computed);
    }

    private static byte[] HashLeaf(byte[] leaf, ManifestHashAlgorithm algorithm) => HashLeaf(leaf.AsSpan(), algorithm);

    private static byte[] HashLeaf(ReadOnlySpan<byte> leaf, ManifestHashAlgorithm algorithm)
    {
        using HashAlgorithm hash = StreamingHashManifest.CreateHashAlgorithm(algorithm);
        hash.TransformBlock(LeafDomainPrefix, 0, LeafDomainPrefix.Length, null, 0);
        byte[] leafBytes = leaf.ToArray();
        hash.TransformFinalBlock(leafBytes, 0, leafBytes.Length);
        return hash.Hash!;
    }

    private static byte[] HashInternal(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right, ManifestHashAlgorithm algorithm)
    {
        using HashAlgorithm hash = StreamingHashManifest.CreateHashAlgorithm(algorithm);
        hash.TransformBlock(InternalDomainPrefix, 0, InternalDomainPrefix.Length, null, 0);
        byte[] leftBytes = left.ToArray();
        byte[] rightBytes = right.ToArray();
        hash.TransformBlock(leftBytes, 0, leftBytes.Length, null, 0);
        hash.TransformFinalBlock(rightBytes, 0, rightBytes.Length);
        return hash.Hash!;
    }

    private static byte[] BuildRoot(byte[][] leaves, ManifestHashAlgorithm algorithm)
    {
        byte[][] current = leaves;
        while (current.Length > 1)
        {
            current = BuildNextLevel(current, algorithm);
        }

        return current[0];
    }

    private static byte[][] BuildNextLevel(byte[][] current, ManifestHashAlgorithm algorithm)
    {
        int nextCount = (current.Length + 1) / 2;
        var next = new byte[nextCount][];
        for (int i = 0; i < nextCount; i++)
        {
            int leftIndex = i * 2;
            int rightIndex = leftIndex + 1;
            if (rightIndex >= current.Length)
            {
                rightIndex = leftIndex;
            }

            next[i] = HashInternal(current[leftIndex], current[rightIndex], algorithm);
        }

        return next;
    }
}
