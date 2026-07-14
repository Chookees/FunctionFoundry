using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace FunctionFoundry.Security;

/// <summary>
/// Output encoding for deterministic pseudonym tokens.
/// </summary>
public enum PseudonymEncoding
{
    /// <summary>Lowercase hexadecimal encoding.</summary>
    Hex = 0,

    /// <summary>URL-safe Base64 without padding.</summary>
    Base64Url = 1,
}

/// <summary>
/// Options controlling deterministic HMAC-based pseudonymization.
/// </summary>
/// <param name="Context">Domain-separation context string. Must not be null or empty.</param>
/// <param name="TenantId">Tenant separation identifier. Must not be null or empty.</param>
/// <param name="KeyVersion">Positive key version embedded in output tokens.</param>
/// <param name="Encoding">Token encoding. Defaults to hexadecimal.</param>
public sealed record PseudonymizerOptions(
    string Context,
    string TenantId,
    int KeyVersion = 1,
    PseudonymEncoding Encoding = PseudonymEncoding.Hex);

/// <summary>
/// Produces deterministic, domain-separated HMAC pseudonyms.
/// </summary>
/// <remarks>
/// <para><b>Important:</b> Pseudonymization is not encryption. Holders of the HMAC key can verify or regenerate tokens
/// for known inputs; the scheme does not provide confidentiality against keyed attackers.</para>
/// <para>Algorithm: HMAC-SHA256 over a canonical length-prefixed payload including context, tenant, version, and value.</para>
/// <para>Thread safety: instances are thread-safe for concurrent generate/verify after construction.</para>
/// <para>Determinism: identical options, key, and input always produce identical tokens.</para>
/// </remarks>
public sealed class Pseudonymizer
{
    private readonly byte[] _key;
    private readonly PseudonymizerOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="Pseudonymizer"/> class.
    /// </summary>
    /// <param name="hmacKey">HMAC key bytes. Copied; must be at least 16 bytes.</param>
    /// <param name="options">Domain separation and encoding options. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when arguments are null.</exception>
    /// <exception cref="ArgumentException">Thrown when the key is too short or options are invalid.</exception>
    public Pseudonymizer(ReadOnlySpan<byte> hmacKey, PseudonymizerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Context);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.TenantId);
        if (options.KeyVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "KeyVersion must be positive.");
        }

        if (hmacKey.Length < 16)
        {
            throw new ArgumentException("HMAC key must be at least 16 bytes.", nameof(hmacKey));
        }

        _key = hmacKey.ToArray();
        _options = options;
    }

    /// <summary>
    /// Generates a versioned pseudonym token for the supplied value.
    /// </summary>
    /// <param name="value">Input identity value. Ownership remains with the caller.</param>
    /// <returns>A token of the form <c>v{version}.{encoded-mac}</c>.</returns>
    /// <example>
    /// <code>
    /// var options = new PseudonymizerOptions("orders", "tenant-a", KeyVersion: 1);
    /// var pseudonymizer = new Pseudonymizer(RandomNumberGenerator.GetBytes(32), options);
    /// string token = pseudonymizer.Pseudonymize(Encoding.UTF8.GetBytes("customer-42"));
    /// </code>
    /// </example>
    public string Pseudonymize(ReadOnlySpan<byte> value)
    {
        byte[] mac = ComputeMac(value);
        try
        {
            string encoded = Encode(mac);
            return FormattableString.Invariant($"v{_options.KeyVersion}.{encoded}");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(mac);
        }
    }

    /// <summary>
    /// Verifies that a token was produced for the given value under the current options and key.
    /// </summary>
    /// <param name="token">Candidate token.</param>
    /// <param name="value">Original value.</param>
    /// <returns><see langword="true"/> when the token matches; otherwise <see langword="false"/>.</returns>
    public bool Verify(string token, ReadOnlySpan<byte> value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        string expected = Pseudonymize(value);
        byte[] left = Encoding.UTF8.GetBytes(token);
        byte[] right = Encoding.UTF8.GetBytes(expected);
        try
        {
            return SecretComparer.FixedTimeEquals(left, right);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(left);
            CryptographicOperations.ZeroMemory(right);
        }
    }

    /// <summary>
    /// Migrates a token from a previous key version by regenerating under the current options.
    /// </summary>
    /// <param name="previous">Pseudonymizer configured with the previous key and version.</param>
    /// <param name="token">Existing token produced by <paramref name="previous"/>.</param>
    /// <param name="value">Original clear value required to re-pseudonymize.</param>
    /// <returns>A new token under the current key version.</returns>
    /// <exception cref="InvalidOperationException">Thrown when verification against <paramref name="previous"/> fails.</exception>
    /// <remarks>
    /// Migration requires the clear value. Tokens alone cannot be re-keyed without an online rematerialization step.
    /// </remarks>
    public string Migrate(Pseudonymizer previous, string token, ReadOnlySpan<byte> value)
    {
        ArgumentNullException.ThrowIfNull(previous);
        if (!previous.Verify(token, value))
        {
            throw new InvalidOperationException("Token failed verification under the previous key.");
        }

        return Pseudonymize(value);
    }

    private byte[] ComputeMac(ReadOnlySpan<byte> value)
    {
        byte[] context = Encoding.UTF8.GetBytes(_options.Context);
        byte[] tenant = Encoding.UTF8.GetBytes(_options.TenantId);
        int length = 4 + context.Length + 4 + tenant.Length + 4 + 4 + value.Length;
        byte[] payload = new byte[length];
        int offset = 0;
        WriteChunk(payload, ref offset, context);
        WriteChunk(payload, ref offset, tenant);
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(offset, 4), _options.KeyVersion);
        offset += 4;
        WriteChunk(payload, ref offset, value);
        try
        {
            return HMACSHA256.HashData(_key, payload);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    private static void WriteChunk(byte[] destination, ref int offset, ReadOnlySpan<byte> data)
    {
        BinaryPrimitives.WriteInt32BigEndian(destination.AsSpan(offset, 4), data.Length);
        offset += 4;
        data.CopyTo(destination.AsSpan(offset));
        offset += data.Length;
    }

    private string Encode(ReadOnlySpan<byte> mac) =>
        _options.Encoding switch
        {
            PseudonymEncoding.Hex => Convert.ToHexStringLower(mac),
            PseudonymEncoding.Base64Url => Base64UrlEncode(mac),
            _ => throw new InvalidOperationException("Unsupported encoding."),
        };

    private static string Base64UrlEncode(ReadOnlySpan<byte> data)
    {
        string base64 = Convert.ToBase64String(data);
        return base64.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
