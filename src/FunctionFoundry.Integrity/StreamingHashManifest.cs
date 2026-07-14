using System.Security.Cryptography;
using System.Text.Json;

namespace FunctionFoundry.Integrity;

/// <summary>
/// Supported digest algorithms for streaming hash manifests.
/// </summary>
public enum ManifestHashAlgorithm
{
    /// <summary>
    /// No algorithm selected. Not valid for manifest operations.
    /// </summary>
    None = 0,

    /// <summary>
    /// SHA-256 (32-byte digest).
    /// </summary>
    Sha256 = 1,

    /// <summary>
    /// SHA-384 (48-byte digest).
    /// </summary>
    Sha384 = 2,
}

/// <summary>
/// Policy controlling which optional manifest metadata fields are permitted.
/// </summary>
/// <param name="AllowFileSize">When <see langword="true"/>, per-entry <c>size</c> metadata is permitted.</param>
/// <param name="AllowCustomMetadata">When <see langword="true"/>, top-level custom metadata keys are permitted.</param>
/// <param name="AllowedCustomMetadataKeys">
/// Optional allow-list for custom metadata keys. When null and <paramref name="AllowCustomMetadata"/> is <see langword="true"/>, any key is allowed.
/// Keys are compared ordinally.
/// </param>
public sealed record ManifestMetadataPolicy(
    bool AllowFileSize = true,
    bool AllowCustomMetadata = false,
    IReadOnlySet<string>? AllowedCustomMetadataKeys = null);

/// <summary>
/// A single manifest entry describing one relative path and digest.
/// </summary>
/// <param name="RelativePath">Normalized relative path using forward slashes.</param>
/// <param name="HashHex">Lowercase hexadecimal digest.</param>
/// <param name="SizeBytes">Optional content size in bytes.</param>
public sealed record ManifestEntry(string RelativePath, string HashHex, long? SizeBytes = null);

/// <summary>
/// Result of manifest verification with detailed mismatch reporting.
/// </summary>
public sealed class ManifestVerificationResult
{
    /// <summary>
    /// Gets whether every entry matched the supplied content.
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// Gets manifest entries that were expected but missing from the supplied content map.
    /// </summary>
    public required IReadOnlyList<string> MissingPaths { get; init; }

    /// <summary>
    /// Gets paths present in the content map but absent from the manifest.
    /// </summary>
    public required IReadOnlyList<string> UnexpectedPaths { get; init; }

    /// <summary>
    /// Gets digest mismatches keyed by relative path.
    /// </summary>
    public required IReadOnlyList<ManifestHashMismatch> HashMismatches { get; init; }

    /// <summary>
    /// Gets size mismatches keyed by relative path.
    /// </summary>
    public required IReadOnlyList<ManifestSizeMismatch> SizeMismatches { get; init; }
}

/// <summary>
/// Describes a digest mismatch for a manifest entry.
/// </summary>
/// <param name="RelativePath">Normalized relative path.</param>
/// <param name="ExpectedHashHex">Digest recorded in the manifest.</param>
/// <param name="ActualHashHex">Digest computed from supplied content.</param>
public sealed record ManifestHashMismatch(string RelativePath, string ExpectedHashHex, string ActualHashHex);

/// <summary>
/// Describes a size mismatch for a manifest entry.
/// </summary>
/// <param name="RelativePath">Normalized relative path.</param>
/// <param name="ExpectedSizeBytes">Size recorded in the manifest.</param>
/// <param name="ActualSizeBytes">Actual content size.</param>
public sealed record ManifestSizeMismatch(string RelativePath, long ExpectedSizeBytes, long ActualSizeBytes);

/// <summary>
/// Builds and verifies deterministic multi-file streaming hash manifests.
/// </summary>
/// <remarks>
/// <para>Manifest wire format: canonical JSON (see <see cref="CanonicalJson"/>) with sorted keys.</para>
/// <para>Schema version: <c>1</c>. Fields: <c>version</c>, <c>hashAlgorithm</c>, <c>entries</c>, optional <c>metadata</c>.</para>
/// <para>Path policy: relative paths only; <c>..</c>, absolute paths, drive letters, and null bytes are rejected.</para>
/// <para>Thread safety: static methods are thread-safe; builder instances are not.</para>
/// </remarks>
public static class StreamingHashManifest
{
    internal const int SchemaVersion = 1;

    /// <summary>
    /// Creates a builder for a new manifest.
    /// </summary>
    /// <param name="algorithm">Digest algorithm used for entry hashing.</param>
    /// <param name="metadataPolicy">Metadata policy enforced during build and verification.</param>
    /// <returns>A mutable builder.</returns>
    public static StreamingHashManifestBuilder CreateBuilder(
        ManifestHashAlgorithm algorithm = ManifestHashAlgorithm.Sha256,
        ManifestMetadataPolicy? metadataPolicy = null) =>
        new(algorithm, metadataPolicy ?? new ManifestMetadataPolicy());

    /// <summary>
    /// Parses a manifest from canonical JSON bytes.
    /// </summary>
    /// <param name="manifestJson">UTF-8 JSON bytes. Will be canonicalized before parsing.</param>
    /// <param name="metadataPolicy">Metadata policy used to validate optional fields.</param>
    /// <returns>Parsed manifest model.</returns>
    /// <exception cref="ArgumentException">Thrown when manifest JSON is invalid or violates policy.</exception>
    public static StreamingHashManifestDocument Parse(
        ReadOnlySpan<byte> manifestJson,
        ManifestMetadataPolicy? metadataPolicy = null) =>
        StreamingHashManifestDocument.Parse(CanonicalJson.Canonicalize(manifestJson), metadataPolicy ?? new ManifestMetadataPolicy());

    /// <summary>
    /// Hashes a readable stream and returns lowercase hexadecimal digest text.
    /// </summary>
    /// <param name="content">Readable content stream.</param>
    /// <param name="algorithm">Digest algorithm.</param>
    /// <param name="bufferSize">Read buffer size. Must be positive.</param>
    /// <returns>Computed digest and content length in bytes.</returns>
    public static (string HashHex, long SizeBytes) HashStream(
        Stream content,
        ManifestHashAlgorithm algorithm = ManifestHashAlgorithm.Sha256,
        int bufferSize = 64 * 1024)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (bufferSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bufferSize), "Buffer size must be positive.");
        }

        using HashAlgorithm hash = CreateHashAlgorithm(algorithm);
        byte[] buffer = GC.AllocateUninitializedArray<byte>(bufferSize);
        long size = 0;
        int read;
        while ((read = content.Read(buffer, 0, buffer.Length)) > 0)
        {
            hash.TransformBlock(buffer, 0, read, null, 0);
            size += read;
        }

        hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return (Convert.ToHexStringLower(hash.Hash!), size);
    }

    /// <summary>
    /// Verifies manifest entries against a path-to-stream map.
    /// </summary>
    /// <param name="manifest">Parsed manifest.</param>
    /// <param name="entries">Map of normalized relative path to readable content.</param>
    /// <returns>Detailed verification result.</returns>
    public static ManifestVerificationResult Verify(
        StreamingHashManifestDocument manifest,
        IReadOnlyDictionary<string, Stream> entries)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(entries);

        var missing = new List<string>();
        var unexpected = new List<string>();
        var hashMismatches = new List<ManifestHashMismatch>();
        var sizeMismatches = new List<ManifestSizeMismatch>();

        var expectedPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (ManifestEntry entry in manifest.Entries)
        {
            expectedPaths.Add(entry.RelativePath);
            if (!entries.TryGetValue(entry.RelativePath, out Stream? stream))
            {
                missing.Add(entry.RelativePath);
                continue;
            }

            (string actualHash, long actualSize) = HashStream(stream, manifest.Algorithm);
            if (!string.Equals(entry.HashHex, actualHash, StringComparison.Ordinal))
            {
                hashMismatches.Add(new ManifestHashMismatch(entry.RelativePath, entry.HashHex, actualHash));
            }

            if (entry.SizeBytes is long expectedSize && expectedSize != actualSize)
            {
                sizeMismatches.Add(new ManifestSizeMismatch(entry.RelativePath, expectedSize, actualSize));
            }
        }

        foreach (string path in entries.Keys)
        {
            string normalized = PathSafety.NormalizeRelativePath(path);
            if (!expectedPaths.Contains(normalized))
            {
                unexpected.Add(normalized);
            }
        }

        bool valid = missing.Count == 0 && unexpected.Count == 0 && hashMismatches.Count == 0 && sizeMismatches.Count == 0;
        return new ManifestVerificationResult
        {
            IsValid = valid,
            MissingPaths = missing,
            UnexpectedPaths = unexpected,
            HashMismatches = hashMismatches,
            SizeMismatches = sizeMismatches,
        };
    }

    internal static HashAlgorithm CreateHashAlgorithm(ManifestHashAlgorithm algorithm) =>
        algorithm switch
        {
            ManifestHashAlgorithm.Sha256 => SHA256.Create(),
            ManifestHashAlgorithm.Sha384 => SHA384.Create(),
            ManifestHashAlgorithm.None => throw new ArgumentException("A hash algorithm must be specified.", nameof(algorithm)),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unsupported hash algorithm."),
        };

    internal static string AlgorithmToWire(ManifestHashAlgorithm algorithm) =>
        algorithm switch
        {
            ManifestHashAlgorithm.Sha256 => "sha256",
            ManifestHashAlgorithm.Sha384 => "sha384",
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unsupported hash algorithm."),
        };

    internal static ManifestHashAlgorithm AlgorithmFromWire(string wire)
    {
        if (string.Equals(wire, "sha256", StringComparison.Ordinal))
        {
            return ManifestHashAlgorithm.Sha256;
        }

        if (string.Equals(wire, "sha384", StringComparison.Ordinal))
        {
            return ManifestHashAlgorithm.Sha384;
        }

        throw new ArgumentException($"Unsupported hash algorithm '{wire}'.", nameof(wire));
    }
}

/// <summary>
/// Mutable builder that accumulates manifest entries and serializes deterministically.
/// </summary>
public sealed class StreamingHashManifestBuilder
{
    private readonly ManifestHashAlgorithm _algorithm;
    private readonly ManifestMetadataPolicy _metadataPolicy;
    private readonly List<ManifestEntry> _entries = [];
    private readonly SortedDictionary<string, string> _metadata = new(StringComparer.Ordinal);

    internal StreamingHashManifestBuilder(ManifestHashAlgorithm algorithm, ManifestMetadataPolicy metadataPolicy)
    {
        _algorithm = algorithm;
        _metadataPolicy = metadataPolicy;
    }

    /// <summary>
    /// Adds a manifest entry by hashing a stream at the specified relative path.
    /// </summary>
    /// <param name="relativePath">Relative path within the manifest root.</param>
    /// <param name="content">Readable content stream positioned at the start.</param>
    /// <param name="includeSize">When <see langword="true"/>, records content length if permitted by metadata policy.</param>
    /// <returns>The added entry.</returns>
    /// <exception cref="ArgumentException">Thrown when the path is unsafe or size metadata is disallowed.</exception>
    public ManifestEntry AddEntry(string relativePath, Stream content, bool includeSize = true)
    {
        string normalized = PathSafety.NormalizeRelativePath(relativePath);
        (string hashHex, long size) = StreamingHashManifest.HashStream(content, _algorithm);
        long? recordedSize = includeSize && _metadataPolicy.AllowFileSize ? size : null;
        if (includeSize && recordedSize is null && _metadataPolicy.AllowFileSize == false)
        {
            throw new ArgumentException("File size metadata is disabled by policy.", nameof(includeSize));
        }

        var entry = new ManifestEntry(normalized, hashHex, recordedSize);
        _entries.Add(entry);
        return entry;
    }

    /// <summary>
    /// Adds a manifest entry with a precomputed digest.
    /// </summary>
    /// <param name="relativePath">Relative path within the manifest root.</param>
    /// <param name="hashHex">Lowercase hexadecimal digest matching the builder algorithm size.</param>
    /// <param name="sizeBytes">Optional size metadata.</param>
    /// <returns>The added entry.</returns>
    public ManifestEntry AddEntry(string relativePath, string hashHex, long? sizeBytes = null)
    {
        string normalized = PathSafety.NormalizeRelativePath(relativePath);
        ValidateHashHex(hashHex, _algorithm);
        if (sizeBytes is not null && !_metadataPolicy.AllowFileSize)
        {
            throw new ArgumentException("File size metadata is disabled by policy.", nameof(sizeBytes));
        }

        var entry = new ManifestEntry(normalized, HexEncoding.NormalizeLowerHex(hashHex), sizeBytes);
        _entries.Add(entry);
        return entry;
    }

    /// <summary>
    /// Adds custom top-level metadata when permitted by policy.
    /// </summary>
    /// <param name="key">Metadata key.</param>
    /// <param name="value">Metadata value.</param>
    /// <exception cref="ArgumentException">Thrown when metadata is disallowed.</exception>
    public void AddMetadata(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        if (!_metadataPolicy.AllowCustomMetadata)
        {
            throw new ArgumentException("Custom metadata is disabled by policy.", nameof(key));
        }

        if (_metadataPolicy.AllowedCustomMetadataKeys is { } allowed && !allowed.Contains(key))
        {
            throw new ArgumentException($"Metadata key '{key}' is not in the allow-list.", nameof(key));
        }

        _metadata[key] = value;
    }

    /// <summary>
    /// Builds canonical UTF-8 manifest JSON bytes.
    /// </summary>
    /// <returns>Deterministic manifest document bytes.</returns>
    public byte[] Build()
    {
        var document = new StreamingHashManifestDocument(
            _algorithm,
            _entries.OrderBy(static e => e.RelativePath, StringComparer.Ordinal).ToArray(),
            _metadata);

        return document.WriteCanonical();
    }

    private static void ValidateHashHex(string hashHex, ManifestHashAlgorithm algorithm)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hashHex);
        int expectedBytes = algorithm switch
        {
            ManifestHashAlgorithm.Sha256 => 32,
            ManifestHashAlgorithm.Sha384 => 48,
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unsupported hash algorithm."),
        };

        if (hashHex.Length != expectedBytes * 2 || !hashHex.All(static c => Uri.IsHexDigit(c)))
        {
            throw new ArgumentException("Hash must be lowercase hexadecimal with the expected digest length.", nameof(hashHex));
        }
    }
}

/// <summary>
/// Parsed streaming hash manifest document.
/// </summary>
public sealed class StreamingHashManifestDocument
{
    /// <summary>
    /// Gets the digest algorithm used by the manifest.
    /// </summary>
    public ManifestHashAlgorithm Algorithm { get; }

    /// <summary>
    /// Gets manifest entries sorted by relative path.
    /// </summary>
    public IReadOnlyList<ManifestEntry> Entries { get; }

    /// <summary>
    /// Gets optional custom metadata sorted by key.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; }

    internal StreamingHashManifestDocument(
        ManifestHashAlgorithm algorithm,
        IReadOnlyList<ManifestEntry> entries,
        IReadOnlyDictionary<string, string> metadata)
    {
        Algorithm = algorithm;
        Entries = entries;
        Metadata = metadata;
    }

    /// <summary>
    /// Serializes the manifest to canonical UTF-8 JSON bytes.
    /// </summary>
    /// <returns>Canonical manifest bytes.</returns>
    public byte[] WriteCanonical()
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false, SkipValidation = true });
        writer.WriteStartObject();
        writer.WriteNumber("version", StreamingHashManifest.SchemaVersion);
        writer.WriteString("hashAlgorithm", StreamingHashManifest.AlgorithmToWire(Algorithm));
        writer.WriteStartArray("entries");
        foreach (ManifestEntry entry in Entries)
        {
            writer.WriteStartObject();
            writer.WriteString("path", entry.RelativePath);
            writer.WriteString("hash", entry.HashHex);
            if (entry.SizeBytes is long size)
            {
                writer.WriteNumber("size", size);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        if (Metadata.Count > 0)
        {
            writer.WriteStartObject("metadata");
            foreach ((string key, string value) in Metadata)
            {
                writer.WriteString(key, value);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndObject();
        writer.Flush();
        return CanonicalJson.Canonicalize(stream.ToArray());
    }

    internal static StreamingHashManifestDocument Parse(ReadOnlySpan<byte> canonicalJson, ManifestMetadataPolicy policy)
    {
        using var doc = JsonDocument.Parse(canonicalJson.ToArray());
        JsonElement root = doc.RootElement;
        if (!root.TryGetProperty("version", out JsonElement versionElement) || versionElement.GetInt32() != StreamingHashManifest.SchemaVersion)
        {
            throw new ArgumentException("Unsupported manifest schema version.", nameof(canonicalJson));
        }

        if (!root.TryGetProperty("hashAlgorithm", out JsonElement algorithmElement))
        {
            throw new ArgumentException("Manifest is missing hashAlgorithm.", nameof(canonicalJson));
        }

        ManifestHashAlgorithm algorithm = StreamingHashManifest.AlgorithmFromWire(algorithmElement.GetString() ?? string.Empty);
        if (!root.TryGetProperty("entries", out JsonElement entriesElement) || entriesElement.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException("Manifest is missing entries array.", nameof(canonicalJson));
        }

        var entries = new List<ManifestEntry>();
        var seenPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement item in entriesElement.EnumerateArray())
        {
            string path = PathSafety.NormalizeRelativePath(item.GetProperty("path").GetString() ?? string.Empty);
            if (!seenPaths.Add(path))
            {
                throw new ArgumentException($"Duplicate manifest entry path '{path}'.", nameof(canonicalJson));
            }

            string hash = item.GetProperty("hash").GetString() ?? string.Empty;
            long? size = item.TryGetProperty("size", out JsonElement sizeElement) ? sizeElement.GetInt64() : null;
            if (size is not null && !policy.AllowFileSize)
            {
                throw new ArgumentException("Manifest contains disallowed size metadata.", nameof(canonicalJson));
            }

            entries.Add(new ManifestEntry(path, HexEncoding.NormalizeLowerHex(hash), size));
        }

        entries.Sort(static (left, right) => string.CompareOrdinal(left.RelativePath, right.RelativePath));

        var metadata = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (root.TryGetProperty("metadata", out JsonElement metadataElement))
        {
            if (!policy.AllowCustomMetadata)
            {
                throw new ArgumentException("Manifest contains disallowed custom metadata.", nameof(canonicalJson));
            }

            foreach (JsonProperty property in metadataElement.EnumerateObject())
            {
                if (policy.AllowedCustomMetadataKeys is { } allowed && !allowed.Contains(property.Name))
                {
                    throw new ArgumentException($"Metadata key '{property.Name}' is not in the allow-list.", nameof(canonicalJson));
                }

                metadata[property.Name] = property.Value.GetString() ?? string.Empty;
            }
        }

        return new StreamingHashManifestDocument(algorithm, entries, metadata);
    }
}

/// <summary>
/// Normalizes and validates relative paths used by integrity manifests.
/// </summary>
internal static class PathSafety
{
    public static string NormalizeRelativePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (path.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("Path must not contain null bytes.", nameof(path));
        }

        string normalized = path.Replace('\\', '/');
        if (Path.IsPathRooted(normalized) || normalized.StartsWith('/'))
        {
            throw new ArgumentException("Path must be relative.", nameof(path));
        }

        if (normalized.Contains(':', StringComparison.Ordinal))
        {
            throw new ArgumentException("Path must not contain drive letters or colons.", nameof(path));
        }

        string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (string segment in segments)
        {
            if (segment == "..")
            {
                throw new ArgumentException("Path must not contain parent traversal segments.", nameof(path));
            }

            if (segment == ".")
            {
                throw new ArgumentException("Path must not contain current-directory segments.", nameof(path));
            }
        }

        return string.Join('/', segments);
    }
}

/// <summary>
/// Hexadecimal encoding helpers for integrity digests.
/// </summary>
internal static class HexEncoding
{
    public static string NormalizeLowerHex(string hex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hex);
        return string.Create(hex.Length, hex, static (destination, source) =>
        {
            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                if (c is >= 'A' and <= 'F')
                {
                    destination[i] = (char)(c + ('a' - 'A'));
                }
                else
                {
                    destination[i] = c;
                }
            }
        });
    }
}
