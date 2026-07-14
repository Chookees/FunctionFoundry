namespace FunctionFoundry.Data;

/// <summary>
/// Options for <see cref="BoundedMemoryExternalMergeSort{T}"/>.
/// </summary>
/// <param name="MemoryBudgetBytes">Maximum in-memory buffer for run accumulation. Must be positive.</param>
/// <param name="StableSort">When true, equal elements retain their original relative order.</param>
/// <param name="TempDirectory">Directory for temp runs. When null, system temp is used.</param>
/// <param name="TempFilePrefix">Prefix for temp run files.</param>
/// <param name="MergeFanIn">Maximum simultaneous open runs during k-way merge. Must be at least 2.</param>
/// <param name="DeleteTempRunsOnSuccess">When true, temp runs are deleted after a successful sort.</param>
public sealed record BoundedMemoryExternalMergeSortOptions(
    int MemoryBudgetBytes = 64 * 1024,
    bool StableSort = false,
    string? TempDirectory = null,
    string TempFilePrefix = "ff-merge-",
    int MergeFanIn = 16,
    bool DeleteTempRunsOnSuccess = true)
{
    /// <summary>
    /// Validates options and returns a normalized copy.
    /// </summary>
    /// <returns>Validated options.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when limits are invalid.</exception>
    public BoundedMemoryExternalMergeSortOptions Validate()
    {
        if (MemoryBudgetBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MemoryBudgetBytes), "Memory budget must be positive.");
        }

        if (MergeFanIn < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(MergeFanIn), "Merge fan-in must be at least 2.");
        }

        if (string.IsNullOrWhiteSpace(TempFilePrefix))
        {
            throw new ArgumentOutOfRangeException(nameof(TempFilePrefix), "Temp file prefix must not be empty.");
        }

        return this;
    }
}

/// <summary>
/// Describes an abandoned temp run discovered during recovery diagnostics.
/// </summary>
/// <param name="FilePath">Absolute path to the temp run file.</param>
/// <param name="CreatedUtc">Best-effort creation timestamp in UTC.</param>
/// <param name="SizeBytes">File size in bytes.</param>
public sealed record AbandonedTempRunDiagnostic(string FilePath, DateTimeOffset CreatedUtc, long SizeBytes);

/// <summary>
/// Result of scanning a directory for abandoned merge-sort temp runs.
/// </summary>
/// <param name="TempDirectory">Directory that was scanned.</param>
/// <param name="Prefix">File prefix filter that was applied.</param>
/// <param name="Runs">Deterministic list ordered by file path.</param>
public sealed record AbandonedTempRunReport(string TempDirectory, string Prefix, IReadOnlyList<AbandonedTempRunDiagnostic> Runs);

/// <summary>
/// Statistics from a completed external merge sort.
/// </summary>
/// <param name="InputCount">Number of input records sorted.</param>
/// <param name="RunCount">Number of temp runs written.</param>
/// <param name="PeakOpenRuns">Peak simultaneously open runs during merge.</param>
public sealed record ExternalMergeSortStatistics(int InputCount, int RunCount, int PeakOpenRuns);
