using FunctionFoundry.Storage.Internal;

namespace FunctionFoundry.Storage;

/// <summary>
/// Performs crash-recoverable transactional updates to a file set using a staging directory and write-ahead journal.
/// </summary>
/// <remarks>
/// <para>Lifecycle: stage files, persist journal, apply replacements, mark journal committed, delete staging.</para>
/// <para>Guarantees: per-file SHA-256 verification, deterministic recovery reports, cancellation between I/O steps.</para>
/// <para>Non-goals: distributed transactions, cross-volume atomic directory trees.</para>
/// <para>Thread safety: one active transaction per instance; separate instances may run concurrently on disjoint targets.</para>
/// </remarks>
public sealed class TransactionalFileSetWriter
{
    private const int JournalFormatVersion = 1;
    private const string StatusPending = "pending";
    private const string StatusCommitting = "committing";
    private const string StatusCommitted = "committed";
    private const string StatusAborted = "aborted";
    private const string OperationUpsert = "upsert";
    private const string OperationDelete = "delete";

    private readonly string _targetDirectory;
    private readonly TransactionalFileSetWriterOptions _options;
    private readonly string _stagingDirectory;
    private readonly string _journalPath;
    private readonly string _transactionId;
    private readonly Dictionary<string, TransactionJournalFileEntry> _entries = new(StringComparer.Ordinal);
    private bool _finalized;

    private TransactionalFileSetWriter(string targetDirectory, string stagingDirectory, string transactionId, TransactionalFileSetWriterOptions options)
    {
        _targetDirectory = targetDirectory;
        _stagingDirectory = stagingDirectory;
        _transactionId = transactionId;
        _options = options;
        _journalPath = Path.Combine(stagingDirectory, options.JournalFileName);
    }

    /// <summary>
    /// Begins a new transaction against the specified target directory.
    /// </summary>
    /// <param name="targetDirectory">Directory that receives committed files. Must exist or be creatable.</param>
    /// <param name="options">Optional writer configuration.</param>
    /// <param name="cancellationToken">Cancellation token checked before I/O begins.</param>
    /// <returns>A writer representing the open transaction.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="targetDirectory"/> is invalid.</exception>
    public static async Task<TransactionalFileSetWriter> BeginAsync(
        string targetDirectory,
        TransactionalFileSetWriterOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        TransactionalFileSetWriterOptions resolved = (options ?? new TransactionalFileSetWriterOptions()).Validate();
        Directory.CreateDirectory(targetDirectory);
        string stagingRoot = resolved.StagingRootDirectory ?? Path.Combine(targetDirectory, ".ff-staging");
        Directory.CreateDirectory(stagingRoot);
        string transactionId = Guid.NewGuid().ToString("N");
        string stagingDirectory = Path.Combine(stagingRoot, transactionId);
        Directory.CreateDirectory(stagingDirectory);
        await WriteJournalAsync(
            stagingDirectory,
            resolved.JournalFileName,
            new TransactionJournalDocument(
                JournalFormatVersion,
                transactionId,
                StatusPending,
                Path.GetFullPath(targetDirectory),
                Array.Empty<TransactionJournalFileEntry>(),
                Array.Empty<string>()),
            cancellationToken).ConfigureAwait(false);
        return new TransactionalFileSetWriter(Path.GetFullPath(targetDirectory), stagingDirectory, transactionId, resolved);
    }

    /// <summary>
    /// Gets the transaction identifier for this open transaction.
    /// </summary>
    public string TransactionId => _transactionId;

    /// <summary>
    /// Gets staged file descriptors in deterministic path order.
    /// </summary>
    public IReadOnlyList<StagedFileDescriptor> GetStagedFiles()
    {
        ThrowIfFinalized();
        return _entries.Values
            .OrderBy(static entry => entry.Path, StringComparer.Ordinal)
            .Select(static entry => new StagedFileDescriptor(entry.Path, entry.Sha256, entry.Size))
            .ToArray();
    }

    /// <summary>
    /// Stages bytes for a relative path inside the target directory.
    /// </summary>
    /// <param name="relativePath">Relative path using forward slashes.</param>
    /// <param name="content">File content.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task StageFileAsync(string relativePath, ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default)
    {
        await using var stream = new MemoryStream(content.ToArray(), writable: false);
        await StageFileAsync(relativePath, stream, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Stages a stream for a relative path inside the target directory.
    /// </summary>
    /// <param name="relativePath">Relative path using forward slashes.</param>
    /// <param name="content">Readable stream positioned at the first byte to hash and persist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task StageFileAsync(string relativePath, Stream content, CancellationToken cancellationToken = default)
    {
        ThrowIfFinalized();
        ArgumentNullException.ThrowIfNull(content);
        cancellationToken.ThrowIfCancellationRequested();
        string normalizedPath = StoragePathNormalizer.NormalizeRelativePath(relativePath);
        string stagedPath = GetStagedPath(normalizedPath);
        StoragePathNormalizer.EnsureWithinRoot(_stagingDirectory, stagedPath);
        string? parent = Path.GetDirectoryName(stagedPath);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        await using var destination = new FileStream(
            stagedPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);
        byte[] buffer = new byte[81920];
        using var incremental = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        long size = 0;
        int read;
        while ((read = await content.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
        {
            size += read;
            incremental.AppendData(buffer.AsSpan(0, read));
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        string sha256 = Convert.ToHexStringLower(incremental.GetHashAndReset());
        _entries[normalizedPath] = new TransactionJournalFileEntry(normalizedPath, sha256, size, OperationUpsert);
        await PersistJournalAsync(StatusPending, Array.Empty<string>(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Stages deletion of a relative path during commit.
    /// </summary>
    /// <param name="relativePath">Relative path using forward slashes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task StageDeleteAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        ThrowIfFinalized();
        cancellationToken.ThrowIfCancellationRequested();
        string normalizedPath = StoragePathNormalizer.NormalizeRelativePath(relativePath);
        string stagedPath = GetStagedPath(normalizedPath);
        if (File.Exists(stagedPath))
        {
            File.Delete(stagedPath);
        }

        _entries[normalizedPath] = new TransactionJournalFileEntry(normalizedPath, string.Empty, 0, OperationDelete);
        await PersistJournalAsync(StatusPending, Array.Empty<string>(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Commits staged changes to the target directory.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Commit metadata.</returns>
    public async Task<TransactionCommitResult> CommitAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfFinalized();
        cancellationToken.ThrowIfCancellationRequested();
        await PersistJournalAsync(StatusCommitting, Array.Empty<string>(), cancellationToken).ConfigureAwait(false);
        bool usedFallback = false;
        var appliedPaths = new List<string>();
        foreach (TransactionJournalFileEntry entry in OrderedEntries())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.Operation == OperationDelete)
            {
                string destination = StoragePathNormalizer.CombineTargetPath(_targetDirectory, entry.Path);
                if (File.Exists(destination))
                {
                    File.Delete(destination);
                }

                appliedPaths.Add(entry.Path);
                await PersistJournalAsync(StatusCommitting, appliedPaths, cancellationToken).ConfigureAwait(false);
                continue;
            }

            string stagedPath = GetStagedPath(entry.Path);
            if (!File.Exists(stagedPath))
            {
                throw new InvalidOperationException($"Staged file '{entry.Path}' is missing.");
            }

            byte[] bytes = await File.ReadAllBytesAsync(stagedPath, cancellationToken).ConfigureAwait(false);
            Sha256Operations.VerifyHexOrThrow(entry.Sha256, bytes);
            if (bytes.Length != entry.Size)
            {
                throw new InvalidOperationException($"Staged file '{entry.Path}' size mismatch.");
            }

            string destinationPath = StoragePathNormalizer.CombineTargetPath(_targetDirectory, entry.Path);
            StoragePathNormalizer.EnsureWithinRoot(_targetDirectory, destinationPath);
            AtomicReplaceMode mode = AtomicFileReplace.Replace(stagedPath, destinationPath, _options.PreferAtomicReplace);
            if (mode == AtomicReplaceMode.CopyAndDelete)
            {
                usedFallback = true;
            }

            appliedPaths.Add(entry.Path);
            await PersistJournalAsync(StatusCommitting, appliedPaths, cancellationToken).ConfigureAwait(false);
        }

        await PersistJournalAsync(StatusCommitted, appliedPaths, cancellationToken).ConfigureAwait(false);
        TryDeleteDirectory(_stagingDirectory);
        _finalized = true;
        return new TransactionCommitResult(_transactionId, _entries.Count, usedFallback);
    }

    /// <summary>
    /// Rolls back the open transaction and removes staging artifacts.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (_finalized)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await PersistJournalAsync(StatusAborted, Array.Empty<string>(), cancellationToken).ConfigureAwait(false);
        TryDeleteDirectory(_stagingDirectory);
        _finalized = true;
    }

    /// <summary>
    /// Gets the staging directory for this open transaction.
    /// </summary>
    public string StagingDirectory => _stagingDirectory;

    /// <summary>
    /// Gets the write-ahead journal path for this open transaction.
    /// </summary>
    public string JournalPath => _journalPath;

    internal async Task PersistJournalForTestingAsync(string status, IReadOnlyList<string> appliedPaths, CancellationToken cancellationToken)
        => await PersistJournalAsync(status, appliedPaths, cancellationToken).ConfigureAwait(false);

    private async Task PersistJournalAsync(string status, IReadOnlyList<string> appliedPaths, CancellationToken cancellationToken)
    {
        var document = new TransactionJournalDocument(
            JournalFormatVersion,
            _transactionId,
            status,
            _targetDirectory,
            OrderedEntries(),
            appliedPaths.Order(StringComparer.Ordinal).ToArray());
        await WriteJournalAsync(_stagingDirectory, _options.JournalFileName, document, cancellationToken).ConfigureAwait(false);
    }

    private TransactionJournalFileEntry[] OrderedEntries()
        => _entries.Values.OrderBy(static entry => entry.Path, StringComparer.Ordinal).ToArray();

    private string GetStagedPath(string normalizedPath)
        => StoragePathNormalizer.CombineTargetPath(_stagingDirectory, normalizedPath);

    private void ThrowIfFinalized()
    {
        if (_finalized)
        {
            throw new InvalidOperationException("The transaction has already been finalized.");
        }
    }

    private static async Task WriteJournalAsync(
        string stagingDirectory,
        string journalFileName,
        TransactionJournalDocument document,
        CancellationToken cancellationToken)
    {
        string journalPath = Path.Combine(stagingDirectory, journalFileName);
        byte[] payload = DeterministicJson.WriteJournal(document);
        string tempPath = journalPath + ".tmp";
        await File.WriteAllBytesAsync(tempPath, payload, cancellationToken).ConfigureAwait(false);
        if (File.Exists(journalPath))
        {
            File.Delete(journalPath);
        }

        File.Move(tempPath, journalPath);
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
