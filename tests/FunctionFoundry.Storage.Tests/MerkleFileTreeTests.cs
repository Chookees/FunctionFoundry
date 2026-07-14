using System.Security.Cryptography;
using System.Text;
using FunctionFoundry.Storage;

namespace FunctionFoundry.Storage.Tests;

public sealed class MerkleFileTreeTests
{
    [Fact]
    public async Task Snapshot_is_deterministic_for_same_tree()
    {
        string root = CreateSampleTree();
        var tree = new MerkleFileTree();
        MerkleFileTreeSnapshot first = await tree.SnapshotAsync(root);
        MerkleFileTreeSnapshot second = await tree.SnapshotAsync(root);
        Assert.Equal(first.ToDeterministicText(), second.ToDeterministicText());
    }

    [Fact]
    public async Task Compare_detects_added_removed_and_modified()
    {
        string root = CreateSampleTree();
        var tree = new MerkleFileTree();
        MerkleFileTreeSnapshot baseline = await tree.SnapshotAsync(root);
        await File.WriteAllTextAsync(Path.Combine(root, "docs", "readme.txt"), "hello v2");
        await File.WriteAllTextAsync(Path.Combine(root, "new.txt"), "new");
        MerkleFileTreeSnapshot current = await tree.SnapshotAsync(root);
        MerkleFileTreeDiff diff = tree.Compare(baseline, current);
        Assert.Contains(diff.Added, entry => entry.NormalizedPath == "new.txt");
        Assert.Contains(diff.Modified, pair => pair.Current.NormalizedPath == "docs/readme.txt");
        Assert.DoesNotContain(diff.Removed, entry => entry.NormalizedPath == "docs/readme.txt");
    }

    [Fact]
    public async Task Compare_detects_moves()
    {
        string baselineRoot = CreateTempDirectory();
        string currentRoot = CreateTempDirectory();
        byte[] payload = Encoding.UTF8.GetBytes("move-me");
        Directory.CreateDirectory(Path.Combine(baselineRoot, "old"));
        await File.WriteAllTextAsync(Path.Combine(baselineRoot, "old", "path.txt"), Encoding.UTF8.GetString(payload));
        Directory.CreateDirectory(Path.Combine(currentRoot, "new"));
        await File.WriteAllTextAsync(Path.Combine(currentRoot, "new", "path.txt"), Encoding.UTF8.GetString(payload));
        var tree = new MerkleFileTree();
        MerkleFileTreeSnapshot baseline = await tree.SnapshotAsync(baselineRoot);
        MerkleFileTreeSnapshot current = await tree.SnapshotAsync(currentRoot);
        MerkleFileTreeDiff diff = tree.Compare(baseline, current);
        Assert.Single(diff.Moved);
        Assert.Equal("old/path.txt", diff.Moved[0].From.NormalizedPath);
        Assert.Equal("new/path.txt", diff.Moved[0].To.NormalizedPath);
    }

    [Fact]
    public async Task Snapshot_skips_staging_directory()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, ".ff-staging", "tx"));
        await File.WriteAllTextAsync(Path.Combine(root, ".ff-staging", "tx", "secret.txt"), "hidden");
        await File.WriteAllTextAsync(Path.Combine(root, "visible.txt"), "ok");
        var tree = new MerkleFileTree();
        MerkleFileTreeSnapshot snapshot = await tree.SnapshotAsync(root);
        Assert.DoesNotContain(snapshot.Entries, entry => entry.NormalizedPath.Contains(".ff-staging", StringComparison.Ordinal));
        Assert.Contains(snapshot.Entries, entry => entry.NormalizedPath == "visible.txt");
    }

    [Fact]
    public async Task Snapshot_records_symlink_target()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        string root = CreateTempDirectory();
        await File.WriteAllTextAsync(Path.Combine(root, "target.txt"), "t");
        string linkPath = Path.Combine(root, "link.txt");
        TryCreateSymlink(Path.Combine(root, "target.txt"), linkPath);
        if (!File.Exists(linkPath))
        {
            return;
        }

        var tree = new MerkleFileTree(new MerkleFileTreeOptions(SymlinkPolicy.RecordTarget));
        MerkleFileTreeSnapshot snapshot = await tree.SnapshotAsync(root);
        Assert.Contains(snapshot.Entries, entry => entry.Kind == MerkleEntryKind.Symlink && entry.NormalizedPath == "link.txt");
    }

    [Fact]
    public async Task Snapshot_follow_detects_symlink_cycle()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        string root = CreateTempDirectory();
        string linkA = Path.Combine(root, "a");
        string linkB = Path.Combine(root, "b");
        TryCreateSymlink(linkB, linkA);
        TryCreateSymlink(linkA, linkB);
        if (!File.Exists(linkA) || !File.Exists(linkB))
        {
            return;
        }

        var tree = new MerkleFileTree(new MerkleFileTreeOptions(SymlinkPolicy.Follow));
        await Assert.ThrowsAnyAsync<Exception>(() => tree.SnapshotAsync(root));
    }

    [Fact]
    public async Task Snapshot_throws_for_missing_root()
    {
        var tree = new MerkleFileTree();
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => tree.SnapshotAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
    }

    [Fact]
    public async Task Compare_ignore_case_finds_modified_paths()
    {
        string baselineRoot = CreateTempDirectory();
        string currentRoot = CreateTempDirectory();
        await File.WriteAllTextAsync(Path.Combine(baselineRoot, "File.txt"), "one");
        await File.WriteAllTextAsync(Path.Combine(currentRoot, "file.txt"), "two");
        var tree = new MerkleFileTree(new MerkleFileTreeOptions(IgnoreCaseForComparison: true));
        MerkleFileTreeDiff diff = tree.Compare(
            await tree.SnapshotAsync(baselineRoot),
            await tree.SnapshotAsync(currentRoot));
        Assert.Single(diff.Modified);
    }

    private static string CreateSampleTree()
    {
        string root = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "docs"));
        File.WriteAllText(Path.Combine(root, "docs", "readme.txt"), "hello");
        return root;
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "ff-merkle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryCreateSymlink(string target, string linkPath)
    {
        try
        {
            if (File.Exists(linkPath))
            {
                File.Delete(linkPath);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(linkPath) ?? ".");
            File.CreateSymbolicLink(linkPath, target);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (PlatformNotSupportedException)
        {
        }
    }
}
