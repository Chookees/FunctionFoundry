namespace FunctionFoundry.Storage;

/// <summary>
/// Options for <see cref="MerkleFileTree"/>.
/// </summary>
/// <param name="SymlinkPolicy">Symbolic link handling policy.</param>
/// <param name="IgnoreCaseForComparison">When true, path keys are compared using invariant upper-case normalization.</param>
public sealed record MerkleFileTreeOptions(
    SymlinkPolicy SymlinkPolicy = SymlinkPolicy.RecordTarget,
    bool IgnoreCaseForComparison = false);

/// <summary>
/// Kind of node represented in a Merkle snapshot entry.
/// </summary>
public enum MerkleEntryKind
{
    /// <summary>Regular file content.</summary>
    File,

    /// <summary>Symbolic link target.</summary>
    Symlink,

    /// <summary>Directory placeholder entry.</summary>
    Directory,
}

/// <summary>
/// A single path entry captured in a Merkle snapshot.
/// </summary>
/// <param name="NormalizedPath">Normalized relative path using forward slashes.</param>
/// <param name="Kind">Entry kind.</param>
/// <param name="ContentHashHex">Lowercase SHA-256 hash describing content or link target.</param>
/// <param name="SizeBytes">File size when applicable; otherwise zero.</param>
public sealed record MerkleFileTreeEntry(
    string NormalizedPath,
    MerkleEntryKind Kind,
    string ContentHashHex,
    long SizeBytes);

/// <summary>
/// Deterministic snapshot of a directory tree.
/// </summary>
/// <param name="RootPath">Absolute root path snapshotted.</param>
/// <param name="RootHashHex">Merkle root over sorted entry hashes.</param>
/// <param name="Entries">Entries sorted by normalized path.</param>
public sealed record MerkleFileTreeSnapshot(
    string RootPath,
    string RootHashHex,
    IReadOnlyList<MerkleFileTreeEntry> Entries)
{
    /// <summary>Gets a deterministic text rendering for tests.</summary>
    public string ToDeterministicText()
    {
        var builder = new System.Text.StringBuilder();
        builder.Append(RootHashHex);
        foreach (MerkleFileTreeEntry entry in Entries)
        {
            builder.Append('\n');
            builder.Append(entry.NormalizedPath);
            builder.Append('|');
            builder.Append(entry.Kind);
            builder.Append('|');
            builder.Append(entry.ContentHashHex);
            builder.Append('|');
            builder.Append(entry.SizeBytes);
        }

        return builder.ToString();
    }
}

/// <summary>
/// Difference between two Merkle snapshots.
/// </summary>
/// <param name="Added">Paths present only in the newer snapshot.</param>
/// <param name="Removed">Paths present only in the baseline snapshot.</param>
/// <param name="Modified">Paths present in both with different content hashes.</param>
/// <param name="Moved">Content hashes that changed path between snapshots.</param>
public sealed record MerkleFileTreeDiff(
    IReadOnlyList<MerkleFileTreeEntry> Added,
    IReadOnlyList<MerkleFileTreeEntry> Removed,
    IReadOnlyList<(MerkleFileTreeEntry Baseline, MerkleFileTreeEntry Current)> Modified,
    IReadOnlyList<(MerkleFileTreeEntry From, MerkleFileTreeEntry To)> Moved)
{
    /// <summary>Gets a deterministic text rendering for tests.</summary>
    public string ToDeterministicText()
    {
        var builder = new System.Text.StringBuilder();
        builder.Append("added:");
        foreach (MerkleFileTreeEntry entry in Added)
        {
            builder.Append('\n');
            builder.Append(entry.NormalizedPath);
        }

        builder.Append("\nremoved:");
        foreach (MerkleFileTreeEntry entry in Removed)
        {
            builder.Append('\n');
            builder.Append(entry.NormalizedPath);
        }

        builder.Append("\nmodified:");
        foreach ((MerkleFileTreeEntry baseline, MerkleFileTreeEntry current) in Modified)
        {
            builder.Append('\n');
            builder.Append(baseline.NormalizedPath);
            builder.Append('|');
            builder.Append(baseline.ContentHashHex);
            builder.Append("->");
            builder.Append(current.ContentHashHex);
        }

        builder.Append("\nmoved:");
        foreach ((MerkleFileTreeEntry from, MerkleFileTreeEntry to) in Moved)
        {
            builder.Append('\n');
            builder.Append(from.NormalizedPath);
            builder.Append("->");
            builder.Append(to.NormalizedPath);
            builder.Append('|');
            builder.Append(from.ContentHashHex);
        }

        return builder.ToString();
    }
}
