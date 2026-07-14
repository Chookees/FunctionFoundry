namespace FunctionFoundry.Storage;

/// <summary>
/// A content-defined chunk produced by <see cref="ContentDefinedChunker"/>.
/// </summary>
/// <param name="Offset">Zero-based offset in the source stream.</param>
/// <param name="Length">Chunk length in bytes.</param>
/// <param name="Sha256Hex">Lowercase SHA-256 hash of chunk bytes.</param>
public sealed record ContentChunk(long Offset, int Length, string Sha256Hex);
