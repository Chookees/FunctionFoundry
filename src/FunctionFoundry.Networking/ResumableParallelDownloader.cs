using System.Net;
using System.Net.Http.Headers;

namespace FunctionFoundry.Networking;

/// <summary>
/// Persisted download checkpoint state.
/// </summary>
/// <param name="Url">Download URL.</param>
/// <param name="TotalSizeBytes">Total content length when known.</param>
/// <param name="ETag">Entity tag from the origin.</param>
/// <param name="LastModified">Last-Modified header value when present.</param>
/// <param name="ChunkSizeBytes">Chunk size used by the active plan.</param>
/// <param name="CompletedChunkIndices">Chunk indices successfully written.</param>
/// <param name="SupportsRanges">Whether range requests are supported.</param>
public sealed record DownloadCheckpoint(
    Uri Url,
    long TotalSizeBytes,
    string? ETag,
    string? LastModified,
    int ChunkSizeBytes,
    IReadOnlyList<int> CompletedChunkIndices,
    bool SupportsRanges);

/// <summary>
/// Result of a download operation.
/// </summary>
/// <param name="BytesWritten">Total bytes written to the destination.</param>
/// <param name="Verified">Whether final digest verification succeeded.</param>
/// <param name="ResumedFromCheckpoint">Whether a prior checkpoint was used.</param>
/// <param name="UsedRangeRequests">Whether range requests were used.</param>
public sealed record DownloadResult(long BytesWritten, bool Verified, bool ResumedFromCheckpoint, bool UsedRangeRequests);

/// <summary>
/// Options for <see cref="ResumableParallelDownloader"/>.
/// </summary>
public sealed class ResumableParallelDownloaderOptions
{
    /// <summary>
    /// Gets or sets the initial chunk size in bytes. Defaults to 256 KiB.
    /// </summary>
    public int InitialChunkSizeBytes { get; set; } = 256 * 1024;

    /// <summary>
    /// Gets or sets the minimum chunk size in bytes. Defaults to 64 KiB.
    /// </summary>
    public int MinChunkSizeBytes { get; set; } = 64 * 1024;

    /// <summary>
    /// Gets or sets the maximum chunk size in bytes. Defaults to 2 MiB.
    /// </summary>
    public int MaxChunkSizeBytes { get; set; } = 2 * 1024 * 1024;

    /// <summary>
    /// Gets or sets the maximum parallel range requests. Defaults to 4.
    /// </summary>
    public int MaxParallelism { get; set; } = 4;

    /// <summary>
    /// Validates option ranges.
    /// </summary>
    public void Validate()
    {
        if (MinChunkSizeBytes <= 0 || MaxChunkSizeBytes < MinChunkSizeBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(MinChunkSizeBytes), "Chunk size bounds are invalid.");
        }

        if (InitialChunkSizeBytes < MinChunkSizeBytes || InitialChunkSizeBytes > MaxChunkSizeBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(InitialChunkSizeBytes), "Initial chunk size must be within min/max bounds.");
        }

        if (MaxParallelism <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxParallelism), "MaxParallelism must be positive.");
        }
    }
}

/// <summary>
/// Persists download checkpoints.
/// </summary>
public interface IDownloadCheckpointStore
{
    /// <summary>
    /// Loads a checkpoint for a URL.
    /// </summary>
    /// <param name="url">Download URL.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Checkpoint state or <see langword="null"/>.</returns>
    Task<DownloadCheckpoint?> LoadAsync(Uri url, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves checkpoint state.
    /// </summary>
    /// <param name="checkpoint">Checkpoint state.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveAsync(DownloadCheckpoint checkpoint, CancellationToken cancellationToken = default);
}

/// <summary>
/// In-memory checkpoint store for tests.
/// </summary>
public sealed class InMemoryDownloadCheckpointStore : IDownloadCheckpointStore
{
    private readonly Dictionary<string, DownloadCheckpoint> _checkpoints = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public Task<DownloadCheckpoint?> LoadAsync(Uri url, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);
        _checkpoints.TryGetValue(url.AbsoluteUri, out DownloadCheckpoint? checkpoint);
        return Task.FromResult(checkpoint);
    }

    /// <inheritdoc />
    public Task SaveAsync(DownloadCheckpoint checkpoint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        _checkpoints[checkpoint.Url.AbsoluteUri] = checkpoint;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Downloads content with resumable HTTP range support and integrity verification.
/// </summary>
public sealed class ResumableParallelDownloader
{
    private readonly HttpClient _httpClient;
    private readonly IDownloadCheckpointStore _checkpointStore;
    private readonly ResumableParallelDownloaderOptions _options;

    /// <summary>
    /// Initializes a new downloader using the caller-provided <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="httpClient">Shared HTTP client instance.</param>
    /// <param name="checkpointStore">Checkpoint persistence abstraction.</param>
    /// <param name="options">Downloader options.</param>
    public ResumableParallelDownloader(
        HttpClient httpClient,
        IDownloadCheckpointStore checkpointStore,
        ResumableParallelDownloaderOptions? options = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _checkpointStore = checkpointStore ?? throw new ArgumentNullException(nameof(checkpointStore));
        _options = options ?? new ResumableParallelDownloaderOptions();
        _options.Validate();
    }

    /// <summary>
    /// Gets the configured options.
    /// </summary>
    public ResumableParallelDownloaderOptions Options => _options;

    /// <summary>
    /// Downloads content into a seekable <paramref name="destination"/> stream.
    /// </summary>
    /// <param name="url">Resource URL.</param>
    /// <param name="destination">Seekable output stream.</param>
    /// <param name="expectedSha256Hex">Expected full-object SHA-256 hex digest.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Download result.</returns>
    public async Task<DownloadResult> DownloadAsync(
        Uri url,
        Stream destination,
        string expectedSha256Hex,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSha256Hex);
        if (!destination.CanSeek)
        {
            throw new ArgumentException("Destination stream must be seekable for range resume.", nameof(destination));
        }

        using HttpRequestMessage probeRequest = new(HttpMethod.Get, url);
        probeRequest.Headers.Range = new RangeHeaderValue(0, 0);
        using HttpResponseMessage probeResponse = await _httpClient.SendAsync(probeRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        bool supportsRanges = probeResponse.StatusCode == HttpStatusCode.PartialContent;
        long totalSize = 0;
        if (supportsRanges && probeResponse.Content.Headers.TryGetValues("Content-Range", out IEnumerable<string>? contentRanges))
        {
            totalSize = ParseTotalSizeFromContentRange(contentRanges.First());
        }
        else
        {
            totalSize = probeResponse.Content.Headers.ContentLength ?? 0;
        }
        string? etag = probeResponse.Headers.ETag?.Tag;
        string? lastModified = probeResponse.Content.Headers.LastModified?.ToString("R");

        DownloadCheckpoint? existing = await _checkpointStore.LoadAsync(url, cancellationToken).ConfigureAwait(false);
        bool resumed = existing is not null;
        if (existing is not null && (existing.ETag != etag || existing.TotalSizeBytes != totalSize))
        {
            existing = null;
            resumed = false;
        }

        int chunkSize = existing?.ChunkSizeBytes ?? _options.InitialChunkSizeBytes;
        TransferPlan plan = TransferPlan.Create(totalSize, chunkSize);
        HashSet<int> completed = existing?.CompletedChunkIndices.ToHashSet() ?? new HashSet<int>();
        using StreamingIntegrityVerifier verifier = new();

        if (!supportsRanges || totalSize <= 0)
        {
            byte[] full = await DownloadFullAsync(url, cancellationToken).ConfigureAwait(false);
            destination.SetLength(0);
            await destination.WriteAsync(full, cancellationToken).ConfigureAwait(false);
            verifier.AppendChunk(0, full);
            FullVerificationResult verification = verifier.VerifyFull(expectedSha256Hex);
            await SaveCheckpointAsync(url, totalSize, etag, lastModified, chunkSize, completed, supportsRanges, cancellationToken).ConfigureAwait(false);
            return new DownloadResult(full.Length, verification.IsValid, resumed, false);
        }

        if (destination.Length != totalSize)
        {
            destination.SetLength(totalSize);
        }

        IReadOnlyList<TransferChunkAssignment> pending = plan.GetPendingChunks(completed);
        using SemaphoreSlim gate = new(_options.MaxParallelism, _options.MaxParallelism);
        List<Task> workers = [];

        foreach (TransferChunkAssignment chunk in pending)
        {
            workers.Add(DownloadChunkAsync(chunk));
        }

        await Task.WhenAll(workers).ConfigureAwait(false);

        foreach (TransferChunkAssignment chunk in plan.Chunks.OrderBy(static c => c.Index))
        {
            destination.Position = chunk.Offset;
            byte[] buffer = new byte[chunk.Length];
            int read = await destination.ReadAsync(buffer.AsMemory(0, chunk.Length), cancellationToken).ConfigureAwait(false);
            if (read > 0)
            {
                verifier.AppendChunk(chunk.Index, buffer.AsSpan(0, read));
            }
        }

        FullVerificationResult fullVerification = verifier.VerifyFull(expectedSha256Hex);
        await SaveCheckpointAsync(url, totalSize, etag, lastModified, chunkSize, completed, supportsRanges, cancellationToken).ConfigureAwait(false);
        return new DownloadResult(totalSize, fullVerification.IsValid, resumed, true);

        async Task DownloadChunkAsync(TransferChunkAssignment chunk)
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using HttpRequestMessage request = new(HttpMethod.Get, url);
                long end = chunk.Offset + chunk.Length - 1;
                request.Headers.Range = new RangeHeaderValue(chunk.Offset, end);
                if (etag is not null)
                {
                    request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(etag));
                }

                using HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                byte[] data = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                if (data.Length != chunk.Length)
                {
                    throw new InvalidOperationException($"Chunk {chunk.Index} length mismatch.");
                }

                destination.Position = chunk.Offset;
                await destination.WriteAsync(data, cancellationToken).ConfigureAwait(false);
                completed.Add(chunk.Index);
            }
            finally
            {
                gate.Release();
            }
        }
    }

    private async Task<byte[]> DownloadFullAsync(Uri url, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task SaveCheckpointAsync(
        Uri url,
        long totalSize,
        string? etag,
        string? lastModified,
        int chunkSize,
        HashSet<int> completed,
        bool supportsRanges,
        CancellationToken cancellationToken)
    {
        DownloadCheckpoint checkpoint = new(
            url,
            totalSize,
            etag,
            lastModified,
            chunkSize,
            completed.OrderBy(static i => i).ToArray(),
            supportsRanges);
        await _checkpointStore.SaveAsync(checkpoint, cancellationToken).ConfigureAwait(false);
    }

    private static long ParseTotalSizeFromContentRange(string contentRange)
    {
        int slash = contentRange.LastIndexOf('/');
        if (slash < 0 || slash == contentRange.Length - 1)
        {
            return 0;
        }

        return long.TryParse(contentRange[(slash + 1)..], out long total) ? total : 0;
    }
}
