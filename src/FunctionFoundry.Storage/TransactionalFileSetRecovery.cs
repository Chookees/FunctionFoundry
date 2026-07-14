using FunctionFoundry.Storage.Internal;

namespace FunctionFoundry.Storage;

/// <summary>
/// Recovers incomplete <see cref="TransactionalFileSetWriter"/> transactions after crashes or restarts.
/// </summary>
/// <remarks>
/// <para>Scans staging directories, validates journals, verifies checksums, and either completes or rolls back work.</para>
/// <para>Reports are deterministic: entries are ordered by path, then action, then detail.</para>
/// </remarks>
public static class TransactionalFileSetRecovery
{
    private const string StatusPending = "pending";
    private const string StatusCommitted = "committed";
    private const string StatusAborted = "aborted";
    private const string OperationUpsert = "upsert";
    private const string OperationDelete = "delete";

    /// <summary>
    /// Recovers all incomplete transactions under the staging root associated with <paramref name="targetDirectory"/>.
    /// </summary>
    /// <param name="targetDirectory">Committed file-set directory.</param>
    /// <param name="options">Optional writer options matching the producer configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Deterministic recovery reports in transaction-id order.</returns>
    public static async Task<IReadOnlyList<RecoveryReport>> RecoverAllAsync(
        string targetDirectory,
        TransactionalFileSetWriterOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        TransactionalFileSetWriterOptions resolved = (options ?? new TransactionalFileSetWriterOptions()).Validate();
        string fullTarget = Path.GetFullPath(targetDirectory);
        string stagingRoot = resolved.StagingRootDirectory ?? Path.Combine(fullTarget, ".ff-staging");
        if (!Directory.Exists(stagingRoot))
        {
            return Array.Empty<RecoveryReport>();
        }

        var reports = new List<RecoveryReport>();
        foreach (string stagingDirectory in Directory.EnumerateDirectories(stagingRoot).OrderBy(static path => path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string journalPath = Path.Combine(stagingDirectory, resolved.JournalFileName);
            if (!File.Exists(journalPath))
            {
                continue;
            }

            RecoveryReport? report = await RecoverSingleAsync(journalPath, resolved, cancellationToken).ConfigureAwait(false);
            if (report is not null)
            {
                reports.Add(report);
            }
        }

        return reports;
    }

    /// <summary>
    /// Recovers a single journal file. Intended for tests and targeted operator tooling.
    /// </summary>
    /// <param name="journalPath">Path to a write-ahead journal file.</param>
    /// <param name="options">Optional writer options matching the producer configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A recovery report, or <see langword="null"/> when no action is required.</returns>
    public static async Task<RecoveryReport?> RecoverFromJournalAsync(
        string journalPath,
        TransactionalFileSetWriterOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalPath);
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(journalPath))
        {
            throw new FileNotFoundException("Journal file was not found.", journalPath);
        }

        return await RecoverSingleAsync(journalPath, (options ?? new TransactionalFileSetWriterOptions()).Validate(), cancellationToken).ConfigureAwait(false);
    }

    private static async Task<RecoveryReport?> RecoverSingleAsync(
        string journalPath,
        TransactionalFileSetWriterOptions options,
        CancellationToken cancellationToken)
    {
        byte[] json = await File.ReadAllBytesAsync(journalPath, cancellationToken).ConfigureAwait(false);
        TransactionJournalDocument document = DeterministicJson.ReadJournal(json);
        if (document.FormatVersion != 1)
        {
            throw new InvalidOperationException($"Unsupported journal format version {document.FormatVersion}.");
        }

        if (string.Equals(document.Status, StatusCommitted, StringComparison.Ordinal)
            || string.Equals(document.Status, StatusAborted, StringComparison.Ordinal))
        {
            return null;
        }

        string stagingDirectory = Path.GetDirectoryName(journalPath)
            ?? throw new InvalidOperationException("Journal path must include a directory.");
        var entries = new List<RecoveryReportEntry>();
        var applied = new HashSet<string>(document.AppliedPaths, StringComparer.Ordinal);

        if (string.Equals(document.Status, StatusPending, StringComparison.Ordinal))
        {
            entries.Add(new RecoveryReportEntry(".", "rollback-staging", document.TransactionId));
            await PersistJournalStatusAsync(journalPath, document, StatusAborted, applied, cancellationToken).ConfigureAwait(false);
            TryDeleteDirectory(stagingDirectory);
            return BuildReport(document.TransactionId, StatusAborted, entries);
        }

        bool usedFallback = false;
        foreach (TransactionJournalFileEntry file in document.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (applied.Contains(file.Path))
            {
                entries.Add(new RecoveryReportEntry(file.Path, "already-applied", file.Operation));
                continue;
            }

            if (file.Operation == OperationDelete)
            {
                string destination = StoragePathNormalizer.CombineTargetPath(document.TargetDirectory, file.Path);
                if (File.Exists(destination))
                {
                    File.Delete(destination);
                }

                entries.Add(new RecoveryReportEntry(file.Path, "delete-applied", string.Empty));
                applied.Add(file.Path);
                continue;
            }

            if (file.Operation != OperationUpsert)
            {
                throw new InvalidOperationException($"Unsupported journal operation '{file.Operation}'.");
            }

            string stagedPath = StoragePathNormalizer.CombineTargetPath(stagingDirectory, file.Path);
            if (!File.Exists(stagedPath))
            {
                entries.Add(new RecoveryReportEntry(file.Path, "missing-staged", "abort"));
                await PersistJournalStatusAsync(journalPath, document, StatusAborted, applied, cancellationToken).ConfigureAwait(false);
                TryDeleteDirectory(stagingDirectory);
                return BuildReport(document.TransactionId, StatusAborted, entries);
            }

            byte[] bytes = await File.ReadAllBytesAsync(stagedPath, cancellationToken).ConfigureAwait(false);
            Sha256Operations.VerifyHexOrThrow(file.Sha256, bytes);
            string destinationPath = StoragePathNormalizer.CombineTargetPath(document.TargetDirectory, file.Path);
            AtomicReplaceMode mode = AtomicFileReplace.Replace(stagedPath, destinationPath, options.PreferAtomicReplace);
            if (mode == AtomicReplaceMode.CopyAndDelete)
            {
                usedFallback = true;
            }

            entries.Add(new RecoveryReportEntry(file.Path, "commit-applied", usedFallback ? "fallback" : "atomic"));
            applied.Add(file.Path);
        }

        await PersistJournalStatusAsync(journalPath, document, StatusCommitted, applied, cancellationToken).ConfigureAwait(false);
        TryDeleteDirectory(stagingDirectory);
        return BuildReport(document.TransactionId, StatusCommitted, entries);
    }

    private static RecoveryReport BuildReport(string transactionId, string status, List<RecoveryReportEntry> entries)
    {
        RecoveryReportEntry[] ordered = entries
            .OrderBy(static entry => entry.Path, StringComparer.Ordinal)
            .ThenBy(static entry => entry.Action, StringComparer.Ordinal)
            .ThenBy(static entry => entry.Detail, StringComparer.Ordinal)
            .ToArray();
        return new RecoveryReport(transactionId, status, ordered);
    }

    private static async Task PersistJournalStatusAsync(
        string journalPath,
        TransactionJournalDocument document,
        string status,
        IReadOnlySet<string> appliedPaths,
        CancellationToken cancellationToken)
    {
        var updated = new TransactionJournalDocument(
            document.FormatVersion,
            document.TransactionId,
            status,
            document.TargetDirectory,
            document.Files,
            appliedPaths.Order(StringComparer.Ordinal).ToArray());
        byte[] payload = DeterministicJson.WriteJournal(updated);
        await File.WriteAllBytesAsync(journalPath, payload, cancellationToken).ConfigureAwait(false);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
