using FunctionFoundry.Storage.Internal;

namespace FunctionFoundry.Storage;

/// <summary>
/// Stores immutable blobs keyed by SHA-256 content hashes with streaming ingestion and safe concurrent writers.
/// </summary>
/// <remarks>
/// <para>Layout: <c>{root}/{objects}/ab/cdef...</c> using the first two hash hex digits as a shard directory.</para>
/// <para>Guarantees: integrity verification on read, temp-and-rename writes, deduplication, non-destructive GC planning.</para>
/// <para>Non-goals: automatic deletion, encryption, remote replication.</para>
/// </remarks>
public sealed class ContentAddressedStore
{
    private readonly string _rootDirectory;
    private readonly string _objectsDirectory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentAddressedStore"/> class.
    /// </summary>
    /// <param name="rootDirectory">Store root directory. Created when missing.</param>
    /// <param name="options">Optional layout configuration.</param>
    public ContentAddressedStore(string rootDirectory, ContentAddressedStoreOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ContentAddressedStoreOptions resolved = (options ?? new ContentAddressedStoreOptions()).Validate();
        _rootDirectory = Path.GetFullPath(rootDirectory);
        _objectsDirectory = Path.Combine(_rootDirectory, resolved.ObjectsSubdirectory.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(_objectsDirectory);
    }

    /// <summary>
    /// Gets the full path to the store root.
    /// </summary>
    public string RootDirectory => _rootDirectory;

    /// <summary>
    /// Ingests bytes and returns content-address metadata.
    /// </summary>
    /// <param name="content">Object bytes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ContentAddressedObjectInfo> PutAsync(ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default)
    {
        await using var stream = new MemoryStream(content.ToArray(), writable: false);
        return await PutAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Streams content into the store, hashing incrementally with bounded memory.
    /// </summary>
    /// <param name="content">Readable stream.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ContentAddressedObjectInfo> PutAsync(Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        cancellationToken.ThrowIfCancellationRequested();
        (string hashHex, long size, bool deduplicated) = await IngestStreamAsync(content, cancellationToken).ConfigureAwait(false);
        return new ContentAddressedObjectInfo(hashHex, size, deduplicated);
    }

    /// <summary>
    /// Opens a stored object for reading after verifying its integrity.
    /// </summary>
    /// <param name="contentHashHex">Expected lowercase SHA-256 hash.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Readable stream positioned at the first object byte.</returns>
    public async Task<Stream> OpenReadAsync(string contentHashHex, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHashHex);
        cancellationToken.ThrowIfCancellationRequested();
        string objectPath = GetObjectPath(contentHashHex);
        if (!File.Exists(objectPath))
        {
            throw new FileNotFoundException("Content-addressed object was not found.", objectPath);
        }

        await VerifyFileHashAsync(objectPath, contentHashHex, cancellationToken).ConfigureAwait(false);
        return new FileStream(objectPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: true);
    }

    /// <summary>
    /// Reads the full object into memory after integrity verification.
    /// </summary>
    /// <param name="contentHashHex">Expected lowercase SHA-256 hash.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<byte[]> ReadAllBytesAsync(string contentHashHex, CancellationToken cancellationToken = default)
    {
        await using Stream stream = await OpenReadAsync(contentHashHex, cancellationToken).ConfigureAwait(false);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
        return memory.ToArray();
    }

    /// <summary>
    /// Returns <see langword="true"/> when an object with the supplied hash exists.
    /// </summary>
    /// <param name="contentHashHex">Lowercase SHA-256 hash.</param>
    public bool Exists(string contentHashHex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHashHex);
        return File.Exists(GetObjectPath(contentHashHex));
    }

    /// <summary>
    /// Plans reclaimable orphan objects that are not referenced by <paramref name="referencedHashes"/>.
    /// </summary>
    /// <param name="referencedHashes">Known live hashes. Comparison is case-sensitive.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Deterministic GC plan without deleting data.</returns>
    public Task<ContentAddressedGcPlan> PlanGarbageCollectionAsync(
        IReadOnlySet<string> referencedHashes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(referencedHashes);
        cancellationToken.ThrowIfCancellationRequested();
        var live = new HashSet<string>(referencedHashes, StringComparer.Ordinal);
        var candidates = new List<ContentAddressedOrphanCandidate>();
        long total = 0;
        foreach (string objectPath in EnumerateObjectFiles().OrderBy(static path => path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relative = Path.GetRelativePath(_rootDirectory, objectPath).Replace('\\', '/');
            string hash = GetHashFromObjectPath(objectPath);
            if (live.Contains(hash))
            {
                continue;
            }

            var info = new FileInfo(objectPath);
            candidates.Add(new ContentAddressedOrphanCandidate(hash, info.Length, relative));
            total += info.Length;
        }

        candidates.Sort(static (left, right) => string.Compare(left.ContentHashHex, right.ContentHashHex, StringComparison.Ordinal));
        return Task.FromResult(new ContentAddressedGcPlan(candidates, total));
    }

    private async Task<(string HashHex, long Size, bool Deduplicated)> IngestStreamAsync(Stream content, CancellationToken cancellationToken)
    {
        string tempPath = Path.Combine(_objectsDirectory, ".tmp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_objectsDirectory);
        string hashHex;
        long size;
        await using (var temp = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            byte[] buffer = new byte[81920];
            using var incremental = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
            size = 0;
            int read;
            while ((read = await content.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
            {
                size += read;
                incremental.AppendData(buffer.AsSpan(0, read));
                await temp.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }

            hashHex = Convert.ToHexStringLower(incremental.GetHashAndReset());
        }

        string finalPath = GetObjectPath(hashHex);
        if (File.Exists(finalPath))
        {
            await VerifyExistingObjectAsync(finalPath, hashHex, size, cancellationToken).ConfigureAwait(false);
            File.Delete(tempPath);
            return (hashHex, size, true);
        }

        string? parent = Path.GetDirectoryName(finalPath);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        try
        {
            File.Move(tempPath, finalPath);
        }
        catch (IOException)
        {
            if (File.Exists(finalPath))
            {
                await VerifyExistingObjectAsync(finalPath, hashHex, size, cancellationToken).ConfigureAwait(false);
                File.Delete(tempPath);
                return (hashHex, size, true);
            }

            throw;
        }

        return (hashHex, size, false);
    }

    private static async Task VerifyExistingObjectAsync(string objectPath, string hashHex, long expectedSize, CancellationToken cancellationToken)
    {
        var info = new FileInfo(objectPath);
        if (info.Length != expectedSize)
        {
            throw new InvalidOperationException($"Existing object '{hashHex}' has unexpected size {info.Length}, expected {expectedSize}.");
        }

        await VerifyFileHashAsync(objectPath, hashHex, cancellationToken).ConfigureAwait(false);
    }

    private static async Task VerifyFileHashAsync(string objectPath, string expectedHashHex, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(objectPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        string actual = await Sha256Operations.ComputeHexAsync(stream, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(expectedHashHex, actual, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Integrity verification failed for '{objectPath}'. Expected {expectedHashHex}, actual {actual}.");
        }
    }

    private string GetObjectPath(string contentHashHex)
        => Sha256Operations.FormatObjectPath(_objectsDirectory, contentHashHex);

    private IEnumerable<string> EnumerateObjectFiles()
    {
        if (!Directory.Exists(_objectsDirectory))
        {
            yield break;
        }

        foreach (string prefixDirectory in Directory.EnumerateDirectories(_objectsDirectory))
        {
            foreach (string file in Directory.EnumerateFiles(prefixDirectory))
            {
                yield return file;
            }
        }
    }

    private static string GetHashFromObjectPath(string objectPath)
    {
        string fileName = Path.GetFileName(objectPath);
        string prefix = Path.GetFileName(Path.GetDirectoryName(objectPath)) ?? string.Empty;
        return prefix + fileName;
    }
}
