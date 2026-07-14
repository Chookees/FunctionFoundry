namespace FunctionFoundry.Networking;

/// <summary>
/// A single transfer chunk assignment.
/// </summary>
/// <param name="Index">Zero-based chunk index.</param>
/// <param name="Offset">Inclusive byte offset in the object.</param>
/// <param name="Length">Chunk length in bytes.</param>
public sealed record TransferChunkAssignment(int Index, long Offset, int Length);

/// <summary>
/// Persisted transfer-plan checkpoint metadata.
/// </summary>
/// <param name="PlanVersion">Plan schema version.</param>
/// <param name="TotalSizeBytes">Total object size in bytes.</param>
/// <param name="ChunkSizeBytes">Nominal chunk size used to build the plan.</param>
/// <param name="CompletedChunkIndices">Chunk indices considered complete.</param>
public sealed record TransferPlanCheckpoint(
    int PlanVersion,
    long TotalSizeBytes,
    int ChunkSizeBytes,
    IReadOnlyList<int> CompletedChunkIndices);

/// <summary>
/// Result of validating a transfer plan.
/// </summary>
/// <param name="HasFullCoverage">Whether chunks cover the full object without gaps.</param>
/// <param name="HasOverlaps">Whether any chunks overlap.</param>
public sealed record TransferPlanValidationResult(bool HasFullCoverage, bool HasOverlaps);

/// <summary>
/// Chunk assignments independent of download execution.
/// </summary>
public sealed class TransferPlan
{
    /// <summary>
    /// Gets the current plan schema version.
    /// </summary>
    public const int CurrentPlanVersion = 1;

    private readonly IReadOnlyList<TransferChunkAssignment> _chunks;

    private TransferPlan(long totalSizeBytes, int chunkSizeBytes, IReadOnlyList<TransferChunkAssignment> chunks)
    {
        TotalSizeBytes = totalSizeBytes;
        ChunkSizeBytes = chunkSizeBytes;
        _chunks = chunks;
    }

    /// <summary>
    /// Gets the total object size in bytes.
    /// </summary>
    public long TotalSizeBytes { get; }

    /// <summary>
    /// Gets the nominal chunk size used to build the plan.
    /// </summary>
    public int ChunkSizeBytes { get; }

    /// <summary>
    /// Gets all chunk assignments in index order.
    /// </summary>
    public IReadOnlyList<TransferChunkAssignment> Chunks => _chunks;

    /// <summary>
    /// Creates a plan that fully covers <paramref name="totalSizeBytes"/> using fixed-size chunks.
    /// </summary>
    /// <param name="totalSizeBytes">Total object size.</param>
    /// <param name="chunkSizeBytes">Chunk size in bytes.</param>
    /// <returns>A transfer plan.</returns>
    public static TransferPlan Create(long totalSizeBytes, int chunkSizeBytes)
    {
        if (totalSizeBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalSizeBytes), "Total size cannot be negative.");
        }

        if (chunkSizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSizeBytes), "Chunk size must be positive.");
        }

        if (totalSizeBytes == 0)
        {
            return new TransferPlan(0, chunkSizeBytes, Array.Empty<TransferChunkAssignment>());
        }

        int chunkCount = (int)((totalSizeBytes + chunkSizeBytes - 1) / chunkSizeBytes);
        var chunks = new TransferChunkAssignment[chunkCount];
        long offset = 0;
        for (int i = 0; i < chunkCount; i++)
        {
            int length = (int)Math.Min(chunkSizeBytes, totalSizeBytes - offset);
            chunks[i] = new TransferChunkAssignment(i, offset, length);
            offset += length;
        }

        TransferPlan plan = new(totalSizeBytes, chunkSizeBytes, chunks);
        TransferPlanValidationResult validation = plan.Validate();
        if (!validation.HasFullCoverage || validation.HasOverlaps)
        {
            throw new InvalidOperationException("Generated transfer plan failed validation.");
        }

        return plan;
    }

    /// <summary>
    /// Validates coverage and overlap constraints.
    /// </summary>
    public TransferPlanValidationResult Validate()
    {
        if (TotalSizeBytes == 0)
        {
            return new TransferPlanValidationResult(true, false);
        }

        bool overlaps = false;
        long expectedOffset = 0;
        foreach (TransferChunkAssignment chunk in _chunks.OrderBy(static c => c.Offset))
        {
            if (chunk.Offset < expectedOffset)
            {
                overlaps = true;
            }

            expectedOffset = Math.Max(expectedOffset, chunk.Offset + chunk.Length);
        }

        bool fullCoverage = expectedOffset == TotalSizeBytes;
        return new TransferPlanValidationResult(fullCoverage, overlaps);
    }

    /// <summary>
    /// Determines whether <paramref name="checkpoint"/> is compatible with this plan.
    /// </summary>
    /// <param name="checkpoint">Checkpoint to validate.</param>
    /// <returns><see langword="true"/> when compatible.</returns>
    public bool IsCheckpointCompatible(TransferPlanCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        return checkpoint.PlanVersion == CurrentPlanVersion
            && checkpoint.TotalSizeBytes == TotalSizeBytes
            && checkpoint.ChunkSizeBytes == ChunkSizeBytes;
    }

    /// <summary>
    /// Returns chunk assignments that are not present in <paramref name="completedChunkIndices"/>.
    /// </summary>
    /// <param name="completedChunkIndices">Completed chunk indices.</param>
    public IReadOnlyList<TransferChunkAssignment> GetPendingChunks(IReadOnlySet<int> completedChunkIndices)
    {
        ArgumentNullException.ThrowIfNull(completedChunkIndices);
        return _chunks.Where(c => !completedChunkIndices.Contains(c.Index)).ToArray();
    }

    /// <summary>
    /// Returns a new plan that reassigns only corrupt chunk indices for repair.
    /// </summary>
    /// <param name="corruptChunkIndices">Chunk indices requiring re-download.</param>
    public IReadOnlyList<TransferChunkAssignment> GetRepairAssignments(IEnumerable<int> corruptChunkIndices)
    {
        ArgumentNullException.ThrowIfNull(corruptChunkIndices);
        HashSet<int> corrupt = corruptChunkIndices.ToHashSet();
        return _chunks.Where(c => corrupt.Contains(c.Index)).ToArray();
    }
}
