namespace FunctionFoundry.Storage;

/// <summary>
/// Metadata describing a stored content-addressed object.
/// </summary>
/// <param name="ContentHashHex">Lowercase SHA-256 content hash.</param>
/// <param name="SizeBytes">Object size in bytes.</param>
/// <param name="WasDeduplicated">True when ingestion skipped writing because the object already existed.</param>
public sealed record ContentAddressedObjectInfo(string ContentHashHex, long SizeBytes, bool WasDeduplicated);

/// <summary>
/// A reclaim candidate identified by <see cref="ContentAddressedStore.PlanGarbageCollectionAsync"/>.
/// </summary>
/// <param name="ContentHashHex">Lowercase SHA-256 hash of the orphan object.</param>
/// <param name="SizeBytes">Object size in bytes.</param>
/// <param name="RelativeObjectPath">Relative path from the store root.</param>
public sealed record ContentAddressedOrphanCandidate(string ContentHashHex, long SizeBytes, string RelativeObjectPath);

/// <summary>
/// Non-destructive garbage-collection plan for a content-addressed store.
/// </summary>
/// <param name="Candidates">Orphan objects ordered by hash.</param>
/// <param name="TotalReclaimableBytes">Sum of candidate sizes.</param>
public sealed record ContentAddressedGcPlan(
    IReadOnlyList<ContentAddressedOrphanCandidate> Candidates,
    long TotalReclaimableBytes)
{
    /// <summary>Gets a deterministic text rendering for logs and tests.</summary>
    public string ToDeterministicText()
    {
        var builder = new System.Text.StringBuilder();
        builder.Append(TotalReclaimableBytes);
        foreach (ContentAddressedOrphanCandidate candidate in Candidates)
        {
            builder.Append('\n');
            builder.Append(candidate.ContentHashHex);
            builder.Append('|');
            builder.Append(candidate.SizeBytes);
            builder.Append('|');
            builder.Append(candidate.RelativeObjectPath);
        }

        return builder.ToString();
    }
}
