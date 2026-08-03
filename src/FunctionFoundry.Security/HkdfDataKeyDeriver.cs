using System.Security.Cryptography;

namespace FunctionFoundry.Security;

/// <summary>
/// Options for <see cref="HkdfDataKeyDeriver"/>.
/// </summary>
/// <param name="MinimumOutputBytes">Minimum accepted derived key length. Defaults to 16.</param>
/// <param name="MaximumOutputBytes">Maximum accepted derived key length. Defaults to 64.</param>
public sealed record HkdfDataKeyDeriverOptions(int MinimumOutputBytes = 16, int MaximumOutputBytes = 64)
{
    /// <summary>
    /// Validates option ranges.
    /// </summary>
    public HkdfDataKeyDeriverOptions Validate()
    {
        if (MinimumOutputBytes <= 0 || MaximumOutputBytes < MinimumOutputBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumOutputBytes), "Output length bounds are invalid.");
        }

        return this;
    }
}

/// <summary>
/// Derives data-encryption keys from a master key using HKDF-SHA256.
/// </summary>
/// <remarks>
/// <para>Uses <see cref="HKDF"/> with <see cref="HashAlgorithmName.SHA256"/>.</para>
/// <para>Non-goals: key storage, envelope wrapping, or key rotation policy (compose with <see cref="EnvelopeEncryptor"/> / <see cref="KeyRotationPlanner"/>).</para>
/// </remarks>
public sealed class HkdfDataKeyDeriver
{
    private readonly HkdfDataKeyDeriverOptions _options;

    /// <summary>
    /// Initializes a new deriver.
    /// </summary>
    /// <param name="options">Optional length limits.</param>
    public HkdfDataKeyDeriver(HkdfDataKeyDeriverOptions? options = null)
    {
        _options = (options ?? new HkdfDataKeyDeriverOptions()).Validate();
    }

    /// <summary>
    /// Derives a 32-byte AES-256 key from <paramref name="masterKey"/>.
    /// </summary>
    /// <param name="masterKey">Input keying material. Must not be empty.</param>
    /// <param name="salt">Optional salt. May be empty.</param>
    /// <param name="info">Optional context/application info. May be empty.</param>
    /// <returns>A newly allocated 32-byte key.</returns>
    public byte[] DeriveAes256Key(ReadOnlySpan<byte> masterKey, ReadOnlySpan<byte> salt, ReadOnlySpan<byte> info)
        => DeriveBytes(masterKey, 32, salt, info);

    /// <summary>
    /// Derives <paramref name="outputLength"/> bytes from <paramref name="masterKey"/>.
    /// </summary>
    /// <param name="masterKey">Input keying material. Must not be empty.</param>
    /// <param name="outputLength">Desired output length in bytes.</param>
    /// <param name="salt">Optional salt. May be empty.</param>
    /// <param name="info">Optional context/application info. May be empty.</param>
    /// <returns>A newly allocated key buffer.</returns>
    public byte[] DeriveBytes(ReadOnlySpan<byte> masterKey, int outputLength, ReadOnlySpan<byte> salt, ReadOnlySpan<byte> info)
    {
        if (masterKey.IsEmpty)
        {
            throw new ArgumentException("Master key must not be empty.", nameof(masterKey));
        }

        if (outputLength < _options.MinimumOutputBytes || outputLength > _options.MaximumOutputBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(outputLength),
                $"Output length must be between {_options.MinimumOutputBytes} and {_options.MaximumOutputBytes}.");
        }

        return HKDF.DeriveKey(HashAlgorithmName.SHA256, masterKey.ToArray(), outputLength, salt.ToArray(), info.ToArray());
    }
}
