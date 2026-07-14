using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FunctionFoundry.Networking;

/// <summary>
/// Result of verifying a single chunk.
/// </summary>
/// <param name="ChunkIndex">Chunk index.</param>
/// <param name="IsValid">Whether the chunk digest matches expectations.</param>
/// <param name="ExpectedHashHex">Expected lowercase hex digest, if known.</param>
/// <param name="ActualHashHex">Actual lowercase hex digest.</param>
public sealed record ChunkVerificationResult(int ChunkIndex, bool IsValid, string? ExpectedHashHex, string ActualHashHex);

/// <summary>
/// Result of verifying a full object digest.
/// </summary>
/// <param name="IsValid">Whether the digest matches.</param>
/// <param name="ExpectedHashHex">Expected lowercase hex digest.</param>
/// <param name="ActualHashHex">Actual lowercase hex digest.</param>
/// <param name="FirstMismatchChunkIndex">First chunk index with a digest mismatch, if any.</param>
public sealed record FullVerificationResult(bool IsValid, string ExpectedHashHex, string ActualHashHex, int? FirstMismatchChunkIndex);

/// <summary>
/// Incrementally verifies chunk and full-object SHA-256 digests with bounded memory.
/// </summary>
public sealed class StreamingIntegrityVerifier : IDisposable
{
    private readonly Dictionary<int, IncrementalHash> _chunkHashes = new();
    private readonly IncrementalHash _fullHash;
    private readonly Dictionary<int, string?> _expectedChunkHashes = new();
    private int? _firstMismatchChunkIndex;
    private bool _disposed;

    /// <summary>
    /// Initializes a new verifier.
    /// </summary>
    public StreamingIntegrityVerifier()
    {
        _fullHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    }

    /// <summary>
    /// Registers an expected digest for a chunk index.
    /// </summary>
    /// <param name="chunkIndex">Chunk index.</param>
    /// <param name="expectedHashHex">Expected lowercase SHA-256 hex digest.</param>
    public void SetExpectedChunkHash(int chunkIndex, string expectedHashHex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedHashHex);
        _expectedChunkHashes[chunkIndex] = NormalizeHex(expectedHashHex);
    }

    /// <summary>
    /// Appends bytes for a chunk and updates full-object state.
    /// </summary>
    /// <param name="chunkIndex">Chunk index.</param>
    /// <param name="data">Chunk bytes.</param>
    public void AppendChunk(int chunkIndex, ReadOnlySpan<byte> data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_chunkHashes.TryGetValue(chunkIndex, out IncrementalHash? chunkHash))
        {
            chunkHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            _chunkHashes[chunkIndex] = chunkHash;
        }

        chunkHash.AppendData(data);
        _fullHash.AppendData(data);
    }

    /// <summary>
    /// Verifies a chunk digest.
    /// </summary>
    /// <param name="chunkIndex">Chunk index.</param>
    /// <returns>Verification result.</returns>
    public ChunkVerificationResult VerifyChunk(int chunkIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_chunkHashes.TryGetValue(chunkIndex, out IncrementalHash? chunkHash))
        {
            throw new InvalidOperationException($"Chunk {chunkIndex} has no data.");
        }

        string actual = ToHex(chunkHash.GetHashAndReset());
        _chunkHashes[chunkIndex] = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        _expectedChunkHashes.TryGetValue(chunkIndex, out string? expected);
        bool valid = expected is null || string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
        if (!valid && _firstMismatchChunkIndex is null)
        {
            _firstMismatchChunkIndex = chunkIndex;
        }

        return new ChunkVerificationResult(chunkIndex, valid, expected, actual);
    }

    /// <summary>
    /// Verifies the full-object digest and reports the first chunk mismatch when chunk expectations exist.
    /// </summary>
    /// <param name="expectedFullHashHex">Expected full-object lowercase SHA-256 hex digest.</param>
    /// <returns>Verification result.</returns>
    public FullVerificationResult VerifyFull(string expectedFullHashHex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedFullHashHex);
        string expected = NormalizeHex(expectedFullHashHex);
        string actual = ToHex(_fullHash.GetHashAndReset());
        bool valid = string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
        return new FullVerificationResult(valid, expected, actual, _firstMismatchChunkIndex);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _fullHash.Dispose();
        foreach (IncrementalHash hash in _chunkHashes.Values)
        {
            hash.Dispose();
        }

        _chunkHashes.Clear();
    }

    private static string NormalizeHex(string hex) => hex.Trim().ToUpperInvariant();

    private static string ToHex(byte[] hash)
    {
        var builder = new StringBuilder(hash.Length * 2);
        foreach (byte b in hash)
        {
            builder.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
