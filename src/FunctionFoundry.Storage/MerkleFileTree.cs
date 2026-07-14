using System.Collections.ObjectModel;
using FunctionFoundry.Storage.Internal;

namespace FunctionFoundry.Storage;

/// <summary>
/// Builds deterministic Merkle snapshots of directory trees and compares them for structural changes.
/// </summary>
/// <remarks>
/// <para>Paths are normalized to forward slashes relative to the snapshot root.</para>
/// <para>Guarantees: symlink cycle protection, explicit symlink policy, deterministic ordering and root hash.</para>
/// </remarks>
public sealed class MerkleFileTree
{
    private readonly MerkleFileTreeOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="MerkleFileTree"/> class.
    /// </summary>
    /// <param name="options">Optional snapshot configuration.</param>
    public MerkleFileTree(MerkleFileTreeOptions? options = null)
    {
        _options = options ?? new MerkleFileTreeOptions();
    }

    /// <summary>
    /// Creates a snapshot of the directory tree rooted at <paramref name="rootDirectory"/>.
    /// </summary>
    /// <param name="rootDirectory">Existing directory to snapshot.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<MerkleFileTreeSnapshot> SnapshotAsync(string rootDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        string fullRoot = Path.GetFullPath(rootDirectory);
        if (!Directory.Exists(fullRoot))
        {
            throw new DirectoryNotFoundException($"Directory '{fullRoot}' was not found.");
        }

        var entries = new List<MerkleFileTreeEntry>();
        var visitedFollow = new HashSet<string>(StringComparer.Ordinal);
        await WalkAsync(fullRoot, fullRoot, entries, visitedFollow, cancellationToken).ConfigureAwait(false);
        entries.Sort(static (left, right) => string.Compare(left.NormalizedPath, right.NormalizedPath, StringComparison.Ordinal));
        string rootHash = ComputeRootHash(entries);
        return new MerkleFileTreeSnapshot(fullRoot, rootHash, new ReadOnlyCollection<MerkleFileTreeEntry>(entries));
    }

    /// <summary>
    /// Compares two snapshots produced with compatible options.
    /// </summary>
    /// <param name="baseline">Older snapshot.</param>
    /// <param name="current">Newer snapshot.</param>
    public MerkleFileTreeDiff Compare(MerkleFileTreeSnapshot baseline, MerkleFileTreeSnapshot current)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);
        var baselineMap = baseline.Entries.ToDictionary(
            entry => NormalizeComparisonKey(entry.NormalizedPath),
            entry => entry,
            StringComparer.Ordinal);
        var currentMap = current.Entries.ToDictionary(
            entry => NormalizeComparisonKey(entry.NormalizedPath),
            entry => entry,
            StringComparer.Ordinal);
        var added = new List<MerkleFileTreeEntry>();
        var removed = new List<MerkleFileTreeEntry>();
        var modified = new List<(MerkleFileTreeEntry Baseline, MerkleFileTreeEntry Current)>();
        foreach (KeyValuePair<string, MerkleFileTreeEntry> pair in currentMap.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            if (!baselineMap.TryGetValue(pair.Key, out MerkleFileTreeEntry? baselineEntry))
            {
                added.Add(pair.Value);
                continue;
            }

            if (!string.Equals(baselineEntry.ContentHashHex, pair.Value.ContentHashHex, StringComparison.Ordinal)
                || baselineEntry.Kind != pair.Value.Kind
                || baselineEntry.SizeBytes != pair.Value.SizeBytes)
            {
                modified.Add((baselineEntry, pair.Value));
            }
        }

        foreach (KeyValuePair<string, MerkleFileTreeEntry> pair in baselineMap.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            if (!currentMap.ContainsKey(pair.Key))
            {
                removed.Add(pair.Value);
            }
        }

        IReadOnlyList<(MerkleFileTreeEntry From, MerkleFileTreeEntry To)> moved = DetectMoves(baseline, current, added, removed, modified);
        added = added.Except(moved.Select(static pair => pair.To)).OrderBy(static entry => entry.NormalizedPath, StringComparer.Ordinal).ToList();
        removed = removed.Except(moved.Select(static pair => pair.From)).OrderBy(static entry => entry.NormalizedPath, StringComparer.Ordinal).ToList();
        modified = modified.OrderBy(static pair => pair.Baseline.NormalizedPath, StringComparer.Ordinal).ToList();
        return new MerkleFileTreeDiff(added, removed, modified, moved);
    }

    private async Task WalkAsync(
        string rootDirectory,
        string currentDirectory,
        List<MerkleFileTreeEntry> entries,
        HashSet<string> visitedFollow,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (string entryPath in Directory.EnumerateFileSystemEntries(currentDirectory).OrderBy(static path => path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = Path.GetFileName(entryPath);
            if (name is ".ff-staging" or ".ff-transaction")
            {
                continue;
            }

            if (IsSymbolicLink(entryPath))
            {
                await ProcessSymlinkAsync(rootDirectory, entryPath, entries, visitedFollow, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (Directory.Exists(entryPath))
            {
                string relative = ToRelativePath(rootDirectory, entryPath);
                entries.Add(new MerkleFileTreeEntry(relative, MerkleEntryKind.Directory, Sha256Operations.ComputeHex(System.Text.Encoding.UTF8.GetBytes(relative)), 0));
                await WalkAsync(rootDirectory, entryPath, entries, visitedFollow, cancellationToken).ConfigureAwait(false);
                continue;
            }

            string fileRelative = ToRelativePath(rootDirectory, entryPath);
            await AddFileEntryAsync(entryPath, fileRelative, entries, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task AddFileEntryAsync(
        string entryPath,
        string fileRelative,
        List<MerkleFileTreeEntry> entries,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(entryPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        string contentHash = await Sha256Operations.ComputeHexAsync(stream, cancellationToken).ConfigureAwait(false);
        long size = new FileInfo(entryPath).Length;
        entries.Add(new MerkleFileTreeEntry(fileRelative, MerkleEntryKind.File, contentHash, size));
    }

    private async Task ProcessSymlinkAsync(
        string rootDirectory,
        string linkPath,
        List<MerkleFileTreeEntry> entries,
        HashSet<string> visitedFollow,
        CancellationToken cancellationToken)
    {
        if (_options.SymlinkPolicy == SymlinkPolicy.Skip)
        {
            return;
        }

        string relative = ToRelativePath(rootDirectory, linkPath);
        if (_options.SymlinkPolicy == SymlinkPolicy.RecordTarget)
        {
            string target = ResolveLinkTarget(linkPath);
            string targetHash = Sha256Operations.ComputeHex(System.Text.Encoding.UTF8.GetBytes(target));
            entries.Add(new MerkleFileTreeEntry(relative, MerkleEntryKind.Symlink, targetHash, 0));
            return;
        }

        string fullLink = Path.GetFullPath(linkPath);
        if (!visitedFollow.Add(fullLink))
        {
            throw new InvalidOperationException($"Symlink cycle detected at '{relative}'.");
        }

        if (Directory.Exists(linkPath))
        {
            entries.Add(new MerkleFileTreeEntry(relative, MerkleEntryKind.Directory, Sha256Operations.ComputeHex(System.Text.Encoding.UTF8.GetBytes(relative)), 0));
            await WalkAsync(rootDirectory, linkPath, entries, visitedFollow, cancellationToken).ConfigureAwait(false);
        }
        else if (File.Exists(linkPath))
        {
            await using var stream = new FileStream(linkPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            string contentHash = await Sha256Operations.ComputeHexAsync(stream, cancellationToken).ConfigureAwait(false);
            long size = new FileInfo(linkPath).Length;
            entries.Add(new MerkleFileTreeEntry(relative, MerkleEntryKind.File, contentHash, size));
        }

        visitedFollow.Remove(fullLink);
    }

    private static bool IsSymbolicLink(string path)
    {
        var info = new FileInfo(path);
        return info.Exists && info.Attributes.HasFlag(FileAttributes.ReparsePoint);
    }

    private static List<(MerkleFileTreeEntry From, MerkleFileTreeEntry To)> DetectMoves(
        MerkleFileTreeSnapshot baseline,
        MerkleFileTreeSnapshot current,
        IReadOnlyList<MerkleFileTreeEntry> added,
        IReadOnlyList<MerkleFileTreeEntry> removed,
        IReadOnlyList<(MerkleFileTreeEntry Baseline, MerkleFileTreeEntry Current)> modified)
    {
        var modifiedPaths = new HashSet<string>(modified.Select(static pair => pair.Baseline.NormalizedPath), StringComparer.Ordinal);
        var removedCandidates = removed
            .Where(entry => entry.Kind == MerkleEntryKind.File && !modifiedPaths.Contains(entry.NormalizedPath))
            .OrderBy(static entry => entry.ContentHashHex, StringComparer.Ordinal)
            .ThenBy(static entry => entry.NormalizedPath, StringComparer.Ordinal)
            .ToList();
        var addedCandidates = added
            .Where(entry => entry.Kind == MerkleEntryKind.File)
            .OrderBy(static entry => entry.ContentHashHex, StringComparer.Ordinal)
            .ThenBy(static entry => entry.NormalizedPath, StringComparer.Ordinal)
            .ToList();
        var moved = new List<(MerkleFileTreeEntry From, MerkleFileTreeEntry To)>();
        var usedAdded = new HashSet<string>(StringComparer.Ordinal);
        foreach (MerkleFileTreeEntry removedEntry in removedCandidates)
        {
            MerkleFileTreeEntry? match = addedCandidates.FirstOrDefault(
                candidate => string.Equals(candidate.ContentHashHex, removedEntry.ContentHashHex, StringComparison.Ordinal)
                    && candidate.SizeBytes == removedEntry.SizeBytes
                    && !usedAdded.Contains(candidate.NormalizedPath));
            if (match is null)
            {
                continue;
            }

            usedAdded.Add(match.NormalizedPath);
            moved.Add((removedEntry, match));
        }

        return moved.OrderBy(static pair => pair.From.NormalizedPath, StringComparer.Ordinal).ToList();
    }

    private string NormalizeComparisonKey(string normalizedPath)
        => StoragePathNormalizer.NormalizeForComparison(normalizedPath, _options.IgnoreCaseForComparison);

    private static string ToRelativePath(string rootDirectory, string fullPath)
    {
        string relative = Path.GetRelativePath(rootDirectory, fullPath).Replace('\\', '/');
        return StoragePathNormalizer.NormalizeRelativePath(relative);
    }

    private static string ResolveLinkTarget(string linkPath)
    {
        var info = new FileInfo(linkPath);
        return info.LinkTarget?.Replace('\\', '/') ?? string.Empty;
    }

    private static string ComputeRootHash(IReadOnlyList<MerkleFileTreeEntry> entries)
    {
        using var incremental = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        foreach (MerkleFileTreeEntry entry in entries)
        {
            string line = $"{entry.NormalizedPath}|{entry.Kind}|{entry.ContentHashHex}|{entry.SizeBytes}\n";
            incremental.AppendData(System.Text.Encoding.UTF8.GetBytes(line));
        }

        return Convert.ToHexStringLower(incremental.GetHashAndReset());
    }
}
