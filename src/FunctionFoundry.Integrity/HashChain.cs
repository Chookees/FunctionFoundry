using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace FunctionFoundry.Integrity;

/// <summary>
/// Policy for optional hash-chain timestamps.
/// </summary>
public enum HashChainTimestampPolicy
{
    /// <summary>
    /// Timestamps are omitted from records.
    /// </summary>
    None = 0,

    /// <summary>
    /// Timestamps may be present and must be UTC ISO-8601 <c>yyyy-MM-ddTHH:mm:ss.fffZ</c> when supplied.
    /// </summary>
    Optional = 1,

    /// <summary>
    /// Every record after genesis must include a UTC ISO-8601 timestamp.
    /// </summary>
    Required = 2,
}

/// <summary>
/// Options controlling hash-chain encoding and verification.
/// </summary>
/// <param name="TimestampPolicy">Timestamp policy applied to records.</param>
/// <param name="RequireMonotonicSequence">When <see langword="true"/>, sequence numbers must increase by exactly one.</param>
public sealed record HashChainOptions(
    HashChainTimestampPolicy TimestampPolicy = HashChainTimestampPolicy.None,
    bool RequireMonotonicSequence = true);

/// <summary>
/// A single append-only hash-chain record.
/// </summary>
/// <param name="Sequence">Monotonic sequence number starting at zero for genesis.</param>
/// <param name="Payload">Canonical UTF-8 payload bytes.</param>
/// <param name="PreviousHashHex">Lowercase hex digest of the previous record, or 64 zero digits for genesis.</param>
/// <param name="RecordHashHex">Lowercase hex digest of this record.</param>
/// <param name="TimestampUtc">Optional UTC timestamp.</param>
public sealed record HashChainRecord(
    long Sequence,
    ReadOnlyMemory<byte> Payload,
    string PreviousHashHex,
    string RecordHashHex,
    DateTimeOffset? TimestampUtc = null);

/// <summary>
/// Describes the first invalid record discovered during chain verification.
/// </summary>
/// <param name="Sequence">Sequence number of the invalid record.</param>
/// <param name="Reason">Human-readable failure reason.</param>
public sealed record HashChainInvalidRecord(long Sequence, string Reason);

/// <summary>
/// Result of hash-chain verification.
/// </summary>
public sealed class HashChainVerificationResult
{
    /// <summary>
    /// Gets whether the full chain is valid.
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// Gets the first invalid record when <see cref="IsValid"/> is <see langword="false"/>.
    /// </summary>
    public HashChainInvalidRecord? FirstInvalidRecord { get; init; }

    /// <summary>
    /// Gets the recomputed head hash when verification succeeds.
    /// </summary>
    public string? HeadHashHex { get; init; }
}

/// <summary>
/// Append-only tamper-evident hash chain over canonical-encoded records.
/// </summary>
/// <remarks>
/// <para>Record hash: SHA-256 over domain-separated canonical JSON of <c>sequence</c>, <c>payload</c> (base64), <c>previousHash</c>, and optional <c>timestamp</c>.</para>
/// <para>Genesis records use a previous-hash of 64 ASCII zero digits.</para>
/// <para><b>Warning:</b> This chain binds payload bytes and append order. It does not provide trusted timestamping, clock integrity, or non-repudiation against an adversary who controls local time sources.</para>
/// <para>Thread safety: static methods are thread-safe.</para>
/// </remarks>
public static class HashChain
{
    private static readonly byte[] RecordDomainPrefix = "FF|HashChain|record\0"u8.ToArray();
    private const string GenesisPreviousHash = "0000000000000000000000000000000000000000000000000000000000000000";

    /// <summary>
    /// Creates the genesis record for a new chain.
    /// </summary>
    /// <param name="payload">Initial payload bytes. Will be canonicalized when JSON.</param>
    /// <param name="options">Chain options.</param>
    /// <param name="timestampUtc">Optional timestamp when permitted or required by policy.</param>
    /// <returns>Genesis record.</returns>
    public static HashChainRecord CreateGenesis(
        ReadOnlySpan<byte> payload,
        HashChainOptions? options = null,
        DateTimeOffset? timestampUtc = null)
    {
        HashChainOptions resolved = options ?? new HashChainOptions();
        ValidateTimestamp(resolved.TimestampPolicy, timestampUtc, isGenesis: true);
        byte[] canonicalPayload = CanonicalizePayload(payload);
        string recordHash = ComputeRecordHash(0, canonicalPayload, GenesisPreviousHash, timestampUtc);
        return new HashChainRecord(0, canonicalPayload, GenesisPreviousHash, recordHash, timestampUtc);
    }

    /// <summary>
    /// Appends a new record after <paramref name="previous"/>.
    /// </summary>
    /// <param name="previous">Previous record in the chain.</param>
    /// <param name="payload">Payload bytes for the new record.</param>
    /// <param name="options">Chain options.</param>
    /// <param name="timestampUtc">Optional timestamp when permitted or required by policy.</param>
    /// <returns>Appended record.</returns>
    public static HashChainRecord Append(
        HashChainRecord previous,
        ReadOnlySpan<byte> payload,
        HashChainOptions? options = null,
        DateTimeOffset? timestampUtc = null)
    {
        ArgumentNullException.ThrowIfNull(previous);
        HashChainOptions resolved = options ?? new HashChainOptions();
        ValidateTimestamp(resolved.TimestampPolicy, timestampUtc, isGenesis: false);
        byte[] canonicalPayload = CanonicalizePayload(payload);
        long sequence = previous.Sequence + 1;
        string recordHash = ComputeRecordHash(sequence, canonicalPayload, previous.RecordHashHex, timestampUtc);
        return new HashChainRecord(sequence, canonicalPayload, previous.RecordHashHex, recordHash, timestampUtc);
    }

    /// <summary>
    /// Verifies an ordered hash chain.
    /// </summary>
    /// <param name="records">Ordered records beginning with genesis at sequence zero.</param>
    /// <param name="options">Chain options.</param>
    /// <returns>Verification result identifying the first invalid record when present.</returns>
    public static HashChainVerificationResult Verify(IReadOnlyList<HashChainRecord> records, HashChainOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (records.Count == 0)
        {
            return new HashChainVerificationResult
            {
                IsValid = false,
                FirstInvalidRecord = new HashChainInvalidRecord(-1, "Chain must contain at least one record."),
            };
        }

        HashChainOptions resolved = options ?? new HashChainOptions();
        string expectedPrevious = GenesisPreviousHash;
        long expectedSequence = 0;

        foreach (HashChainRecord record in records)
        {
            if (record.Sequence != expectedSequence)
            {
                return Invalid(record.Sequence, resolved.RequireMonotonicSequence
                    ? $"Expected sequence {expectedSequence} but found {record.Sequence}."
                    : $"Unexpected sequence {record.Sequence}.");
            }

            if (!string.Equals(record.PreviousHashHex, expectedPrevious, StringComparison.Ordinal))
            {
                return Invalid(record.Sequence, "Previous hash does not match the prior record.");
            }

            try
            {
                ValidateTimestamp(resolved.TimestampPolicy, record.TimestampUtc, record.Sequence == 0);
            }
            catch (ArgumentException ex)
            {
                return Invalid(record.Sequence, ex.Message);
            }

            string computed = ComputeRecordHash(record.Sequence, record.Payload.Span, record.PreviousHashHex, record.TimestampUtc);
            if (!string.Equals(record.RecordHashHex, computed, StringComparison.Ordinal))
            {
                return Invalid(record.Sequence, "Record hash does not match canonical encoding.");
            }

            expectedPrevious = record.RecordHashHex;
            expectedSequence = record.Sequence + 1;
        }

        return new HashChainVerificationResult
        {
            IsValid = true,
            HeadHashHex = expectedPrevious,
        };
    }

    /// <summary>
    /// Serializes a record to canonical UTF-8 JSON bytes without the <c>recordHash</c> field.
    /// </summary>
    /// <param name="record">Record to serialize.</param>
    /// <returns>Canonical JSON bytes.</returns>
    public static byte[] SerializeRecordBody(HashChainRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false, SkipValidation = true });
        WriteRecordBody(writer, record.Sequence, record.Payload.Span, record.PreviousHashHex, record.TimestampUtc);
        writer.Flush();
        return CanonicalJson.Canonicalize(stream.ToArray());
    }

    private static HashChainVerificationResult Invalid(long sequence, string reason) =>
        new()
        {
            IsValid = false,
            FirstInvalidRecord = new HashChainInvalidRecord(sequence, reason),
        };

    private static byte[] CanonicalizePayload(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty)
        {
            return Array.Empty<byte>();
        }

        if (payload[0] is (byte)'{' or (byte)'[')
        {
            return CanonicalJson.Canonicalize(payload);
        }

        return payload.ToArray();
    }

    private static string ComputeRecordHash(
        long sequence,
        ReadOnlySpan<byte> payload,
        string previousHashHex,
        DateTimeOffset? timestampUtc)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false, SkipValidation = true });
        WriteRecordBody(writer, sequence, payload, previousHashHex, timestampUtc);
        writer.Flush();
        byte[] canonicalBody = CanonicalJson.Canonicalize(stream.ToArray());

        using var hash = SHA256.Create();
        hash.TransformBlock(RecordDomainPrefix, 0, RecordDomainPrefix.Length, null, 0);
        hash.TransformFinalBlock(canonicalBody, 0, canonicalBody.Length);
        return Convert.ToHexStringLower(hash.Hash!);
    }

    private static void WriteRecordBody(
        Utf8JsonWriter writer,
        long sequence,
        ReadOnlySpan<byte> payload,
        string previousHashHex,
        DateTimeOffset? timestampUtc)
    {
        writer.WriteStartObject();
        writer.WriteNumber("sequence", sequence);
        writer.WriteString("payload", Convert.ToBase64String(payload));
        writer.WriteString("previousHash", previousHashHex);
        if (timestampUtc is DateTimeOffset timestamp)
        {
            writer.WriteString("timestamp", timestamp.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
        }

        writer.WriteEndObject();
    }

    private static void ValidateTimestamp(HashChainTimestampPolicy policy, DateTimeOffset? timestampUtc, bool isGenesis)
    {
        switch (policy)
        {
            case HashChainTimestampPolicy.None:
                if (timestampUtc is not null)
                {
                    throw new ArgumentException("Timestamp is not permitted by policy.");
                }

                break;
            case HashChainTimestampPolicy.Optional:
                if (timestampUtc is not null)
                {
                    EnsureUtcIso(timestampUtc.Value);
                }

                break;
            case HashChainTimestampPolicy.Required:
                if (!isGenesis && timestampUtc is null)
                {
                    throw new ArgumentException("Timestamp is required by policy.");
                }

                if (timestampUtc is not null)
                {
                    EnsureUtcIso(timestampUtc.Value);
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(policy), policy, "Unsupported timestamp policy.");
        }
    }

    private static void EnsureUtcIso(DateTimeOffset timestampUtc)
    {
        if (timestampUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamp must use UTC offset +00:00.");
        }
    }
}
