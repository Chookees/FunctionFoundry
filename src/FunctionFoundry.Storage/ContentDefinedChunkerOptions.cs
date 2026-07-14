namespace FunctionFoundry.Storage;

/// <summary>
/// Options for <see cref="ContentDefinedChunker"/>.
/// </summary>
/// <param name="MinimumChunkSize">Minimum bytes before a Rabin boundary can cut.</param>
/// <param name="TargetChunkSize">Desired average chunk size used to derive the boundary mask.</param>
/// <param name="MaximumChunkSize">Hard maximum chunk size.</param>
/// <param name="WindowSize">Rabin rolling-hash window size in bytes.</param>
public sealed record ContentDefinedChunkerOptions(
    int MinimumChunkSize = 16 * 1024,
    int TargetChunkSize = 64 * 1024,
    int MaximumChunkSize = 256 * 1024,
    int WindowSize = 48)
{
    /// <summary>Gets a validated copy of these options.</summary>
    public ContentDefinedChunkerOptions Validate()
    {
        if (MinimumChunkSize <= 0 || TargetChunkSize <= 0 || MaximumChunkSize <= 0 || WindowSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumChunkSize), "Chunk sizes and window must be positive.");
        }

        if (MinimumChunkSize > TargetChunkSize || TargetChunkSize > MaximumChunkSize)
        {
            throw new ArgumentOutOfRangeException(nameof(TargetChunkSize), "Chunk sizes must satisfy minimum <= target <= maximum.");
        }

        return this;
    }

    internal uint BoundaryMask => ComputeBoundaryMask(TargetChunkSize);

    private static uint ComputeBoundaryMask(int targetChunkSize)
    {
        int shift = 0;
        while ((1 << shift) < targetChunkSize && shift < 31)
        {
            shift++;
        }

        return shift == 0 ? 0U : (1U << shift) - 1U;
    }
}
