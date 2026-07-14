using System.Security.Cryptography;
using System.Text;
using FunctionFoundry.Storage;
using FunctionFoundry.Storage.Internal;

namespace FunctionFoundry.Storage.Tests;

public sealed class TransactionalFileSetWriterTests
{
    [Fact]
    public async Task Commit_writes_files_with_checksums()
    {
        string root = CreateTempDirectory();
        var writer = await TransactionalFileSetWriter.BeginAsync(root);
        await writer.StageFileAsync("docs/readme.txt", "hello"u8.ToArray());
        TransactionCommitResult result = await writer.CommitAsync();
        Assert.Equal(1, result.FileCount);
        Assert.True(File.Exists(Path.Combine(root, "docs", "readme.txt")));
        Assert.Equal("hello", await File.ReadAllTextAsync(Path.Combine(root, "docs", "readme.txt")));
    }

    [Fact]
    public async Task Rollback_discards_staged_files()
    {
        string root = CreateTempDirectory();
        var writer = await TransactionalFileSetWriter.BeginAsync(root);
        await writer.StageFileAsync("a.txt", "x"u8.ToArray());
        await writer.RollbackAsync();
        Assert.False(File.Exists(Path.Combine(root, "a.txt")));
        Assert.False(Directory.Exists(writer.StagingDirectory));
    }

    [Fact]
    public async Task StageDelete_removes_file_on_commit()
    {
        string root = CreateTempDirectory();
        await File.WriteAllTextAsync(Path.Combine(root, "old.txt"), "old");
        var writer = await TransactionalFileSetWriter.BeginAsync(root);
        await writer.StageDeleteAsync("old.txt");
        await writer.CommitAsync();
        Assert.False(File.Exists(Path.Combine(root, "old.txt")));
    }

    [Fact]
    public async Task GetStagedFiles_is_deterministically_ordered()
    {
        string root = CreateTempDirectory();
        var writer = await TransactionalFileSetWriter.BeginAsync(root);
        await writer.StageFileAsync("b.txt", "b"u8.ToArray());
        await writer.StageFileAsync("a.txt", "a"u8.ToArray());
        IReadOnlyList<StagedFileDescriptor> staged = writer.GetStagedFiles();
        Assert.Equal(["a.txt", "b.txt"], staged.Select(static file => file.RelativePath));
    }

    [Fact]
    public async Task Commit_throws_after_finalized()
    {
        string root = CreateTempDirectory();
        var writer = await TransactionalFileSetWriter.BeginAsync(root);
        await writer.StageFileAsync("a.txt", "a"u8.ToArray());
        await writer.CommitAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.StageFileAsync("b.txt", "b"u8.ToArray()));
    }

    [Fact]
    public void NormalizeRelativePath_rejects_traversal()
    {
        Assert.Throws<ArgumentException>(() => StoragePathNormalizer.NormalizeRelativePath("../secret"));
    }

    [Fact]
    public async Task Recovery_pending_journal_rolls_back_deterministically()
    {
        string root = CreateTempDirectory();
        var writer = await TransactionalFileSetWriter.BeginAsync(root);
        await writer.StageFileAsync("pending.txt", "data"u8.ToArray());
        await writer.PersistJournalForTestingAsync("pending", Array.Empty<string>(), CancellationToken.None);
        RecoveryReport? report = await TransactionalFileSetRecovery.RecoverFromJournalAsync(writer.JournalPath);
        Assert.NotNull(report);
        Assert.Equal("aborted", report!.FinalStatus);
        Assert.Equal(
            $"{report.TransactionId}|aborted\n.|rollback-staging|{report.TransactionId}",
            report.ToDeterministicText());
        Assert.False(File.Exists(Path.Combine(root, "pending.txt")));
    }

    [Fact]
    public async Task Recovery_committing_journal_completes_partial_commit()
    {
        string root = CreateTempDirectory();
        var writer = await TransactionalFileSetWriter.BeginAsync(root);
        await writer.StageFileAsync("first.txt", "one"u8.ToArray());
        await writer.StageFileAsync("second.txt", "two"u8.ToArray());
        await File.WriteAllTextAsync(Path.Combine(root, "first.txt"), "one");
        await writer.PersistJournalForTestingAsync("committing", ["first.txt"], CancellationToken.None);
        string stagedSecond = Path.Combine(writer.StagingDirectory, "second.txt");
        Assert.True(File.Exists(stagedSecond));
        RecoveryReport? report = await TransactionalFileSetRecovery.RecoverFromJournalAsync(writer.JournalPath);
        Assert.NotNull(report);
        Assert.Equal("committed", report!.FinalStatus);
        Assert.Equal("one", await File.ReadAllTextAsync(Path.Combine(root, "first.txt")));
        Assert.Equal("two", await File.ReadAllTextAsync(Path.Combine(root, "second.txt")));
        Assert.Contains(report.Entries, entry => entry.Path == "first.txt" && entry.Action == "already-applied");
        Assert.Contains(report.Entries, entry => entry.Path == "second.txt" && entry.Action == "commit-applied");
    }

    [Fact]
    public async Task Recovery_missing_staged_file_aborts()
    {
        string root = CreateTempDirectory();
        var writer = await TransactionalFileSetWriter.BeginAsync(root);
        await writer.StageFileAsync("gone.txt", "x"u8.ToArray());
        await writer.PersistJournalForTestingAsync("committing", Array.Empty<string>(), CancellationToken.None);
        File.Delete(Path.Combine(writer.StagingDirectory, "gone.txt"));
        RecoveryReport? report = await TransactionalFileSetRecovery.RecoverFromJournalAsync(writer.JournalPath);
        Assert.NotNull(report);
        Assert.Equal("aborted", report!.FinalStatus);
        Assert.Contains(report.Entries, entry => entry.Action == "missing-staged");
    }

    [Fact]
    public async Task Commit_honors_cancellation()
    {
        string root = CreateTempDirectory();
        var writer = await TransactionalFileSetWriter.BeginAsync(root);
        await writer.StageFileAsync("a.txt", Encoding.UTF8.GetBytes(new string('a', 1024)));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer.CommitAsync(cts.Token));
    }

    [Fact]
    public async Task RecoverAll_processes_multiple_transactions_in_order()
    {
        string root = CreateTempDirectory();
        var first = await TransactionalFileSetWriter.BeginAsync(root);
        await first.StageFileAsync("a.txt", "a"u8.ToArray());
        await first.PersistJournalForTestingAsync("pending", Array.Empty<string>(), CancellationToken.None);
        var second = await TransactionalFileSetWriter.BeginAsync(root);
        await second.StageFileAsync("b.txt", "b"u8.ToArray());
        await second.PersistJournalForTestingAsync("committing", Array.Empty<string>(), CancellationToken.None);
        IReadOnlyList<RecoveryReport> reports = await TransactionalFileSetRecovery.RecoverAllAsync(root);
        Assert.Equal(2, reports.Count);
        Assert.True(string.CompareOrdinal(reports[0].TransactionId, reports[1].TransactionId) < 0);
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "ff-storage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
