namespace FunctionFoundry.Storage;

/// <summary>
/// Result of committing a transactional file set.
/// </summary>
/// <param name="TransactionId">Committed transaction identifier.</param>
/// <param name="FileCount">Number of files committed.</param>
/// <param name="UsedFallbackReplace">True when any file used copy-and-delete fallback instead of atomic move.</param>
public sealed record TransactionCommitResult(string TransactionId, int FileCount, bool UsedFallbackReplace);

/// <summary>
/// Describes a file staged for commit.
/// </summary>
/// <param name="RelativePath">Normalized relative path inside the target directory.</param>
/// <param name="Sha256Hex">Lowercase SHA-256 checksum of staged content.</param>
/// <param name="SizeBytes">Staged content length in bytes.</param>
public sealed record StagedFileDescriptor(string RelativePath, string Sha256Hex, long SizeBytes);
