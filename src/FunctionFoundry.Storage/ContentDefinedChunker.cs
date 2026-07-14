using FunctionFoundry.Storage.Internal;

namespace FunctionFoundry.Storage;

/// <summary>
/// Splits streams into content-defined chunks using a Rabin-style rolling hash.
/// </summary>
/// <remarks>
/// <para>Chunk boundaries appear when the rolling hash matches a mask derived from <see cref="ContentDefinedChunkerOptions.TargetChunkSize"/>.</para>
/// <para>Guarantees: bounded memory via fixed buffers, deterministic chunking for identical content and options.</para>
/// </remarks>
public sealed class ContentDefinedChunker
{
    private readonly ContentDefinedChunkerOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentDefinedChunker"/> class.
    /// </summary>
    /// <param name="options">Optional chunking configuration.</param>
    public ContentDefinedChunker(ContentDefinedChunkerOptions? options = null)
    {
        _options = (options ?? new ContentDefinedChunkerOptions()).Validate();
    }

    /// <summary>
    /// Chunks a byte buffer.
    /// </summary>
    /// <param name="content">Input bytes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<IReadOnlyList<ContentChunk>> ChunkAsync(ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<ContentChunk>>(ChunkBuffer(content.Span));
    }

    /// <summary>
    /// Chunks a readable stream sequentially.
    /// </summary>
    /// <param name="content">Input stream.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<ContentChunk>> ChunkAsync(Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        cancellationToken.ThrowIfCancellationRequested();
        using var memory = new MemoryStream();
        await content.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
        return ChunkBuffer(memory.ToArray());
    }

    private List<ContentChunk> ChunkBuffer(ReadOnlySpan<byte> content)
    {
        var chunks = new List<ContentChunk>();
        if (content.Length == 0)
        {
            return chunks;
        }

        var rolling = new RabinRollingHash(_options.WindowSize);
        uint mask = _options.BoundaryMask;
        int chunkStart = 0;
        int sinceBoundary = 0;
        using var incremental = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        for (int index = 0; index < content.Length; index++)
        {
            byte value = content[index];
            incremental.AppendData([value]);
            sinceBoundary++;
            ulong hash = rolling.Push(value);
            bool atMax = sinceBoundary >= _options.MaximumChunkSize;
            bool atBoundary = sinceBoundary >= _options.MinimumChunkSize && (mask == 0 || ((uint)hash & mask) == 0);
            bool isLast = index == content.Length - 1;
            if (atMax || atBoundary || isLast)
            {
                string sha = Convert.ToHexStringLower(incremental.GetHashAndReset());
                chunks.Add(new ContentChunk(chunkStart, sinceBoundary, sha));
                chunkStart = index + 1;
                sinceBoundary = 0;
            }
        }

        return chunks;
    }
}
