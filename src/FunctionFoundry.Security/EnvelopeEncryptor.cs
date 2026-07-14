using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace FunctionFoundry.Security;

/// <summary>
/// Options for versioned AES-GCM envelope encryption.
/// </summary>
/// <param name="MaximumPlaintextBytes">Maximum accepted plaintext length in bytes. Must be positive.</param>
/// <param name="MaximumCiphertextBytes">Maximum accepted ciphertext envelope length in bytes. Must be positive.</param>
public sealed record EnvelopeEncryptionOptions(
    int MaximumPlaintextBytes = 16 * 1024 * 1024,
    int MaximumCiphertextBytes = 16 * 1024 * 1024 + 4096);

/// <summary>
/// Performs versioned AES-GCM envelope encryption with key identifiers and associated authenticated data.
/// </summary>
/// <remarks>
/// <para>Algorithm: AES-256-GCM via <see cref="AesGcm"/>.</para>
/// <para>Wire format (big-endian): magic (4) | version (1) | keyIdLen (2) | keyId UTF-8 | nonce (12) | tag (16) | ciphertext.</para>
/// <para>Guarantees: authentication failures never return plaintext; owned intermediate buffers are cleared.</para>
/// <para>Non-guarantees: no formal security proof; key resolver confidentiality is the host's responsibility.</para>
/// <para>Thread safety: instance methods are thread-safe provided the resolver is thread-safe.</para>
/// </remarks>
public sealed class EnvelopeEncryptor
{
    private const uint Magic = 0x46464531; // "FFE1"
    private const byte FormatVersion = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int HeaderFixedSize = 4 + 1 + 2 + NonceSize + TagSize;

    private readonly IDataKeyResolver _resolver;
    private readonly EnvelopeEncryptionOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="EnvelopeEncryptor"/> class.
    /// </summary>
    /// <param name="resolver">Key resolver used for encryption and decryption. Must not be null.</param>
    /// <param name="options">Optional size limits. When null, defaults are used.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resolver"/> is null.</exception>
    public EnvelopeEncryptor(IDataKeyResolver resolver, EnvelopeEncryptionOptions? options = null)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _options = options ?? new EnvelopeEncryptionOptions();
        if (_options.MaximumPlaintextBytes <= 0 || _options.MaximumCiphertextBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Size limits must be positive.");
        }
    }

    /// <summary>
    /// Encrypts plaintext under the specified key identifier.
    /// </summary>
    /// <param name="keyId">Key identifier resolved through <see cref="IDataKeyResolver"/>. Must not be null or empty.</param>
    /// <param name="plaintext">Plaintext bytes to encrypt. Ownership remains with the caller.</param>
    /// <param name="associatedData">Optional AAD authenticated but not encrypted. May be empty.</param>
    /// <returns>A newly allocated envelope byte array.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="keyId"/> is invalid or the key is not 32 bytes.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when plaintext exceeds configured limits.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the key cannot be resolved.</exception>
    /// <example>
    /// <code>
    /// var resolver = new DictionaryDataKeyResolver(new Dictionary&lt;string, byte[]&gt;
    /// {
    ///     ["k1"] = RandomNumberGenerator.GetBytes(32),
    /// });
    /// var encryptor = new EnvelopeEncryptor(resolver);
    /// byte[] envelope = encryptor.Encrypt("k1", Encoding.UTF8.GetBytes("secret"), ReadOnlySpan&lt;byte&gt;.Empty);
    /// </code>
    /// </example>
    public byte[] Encrypt(string keyId, ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        if (plaintext.Length > _options.MaximumPlaintextBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(plaintext), "Plaintext exceeds configured maximum.");
        }

        byte[] keyIdBytes = Encoding.UTF8.GetBytes(keyId);
        if (keyIdBytes.Length > ushort.MaxValue)
        {
            throw new ArgumentException("Key identifier is too long.", nameof(keyId));
        }

        if (!_resolver.TryResolve(keyId, out byte[] key))
        {
            throw new InvalidOperationException($"Key '{keyId}' could not be resolved.");
        }

        try
        {
            ValidateAes256Key(key);
            byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
            byte[] ciphertext = new byte[plaintext.Length];
            byte[] tag = new byte[TagSize];
            using (var aes = new AesGcm(key, TagSize))
            {
                aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
            }

            int total = HeaderFixedSize + keyIdBytes.Length + ciphertext.Length;
            if (total > _options.MaximumCiphertextBytes)
            {
                throw new ArgumentOutOfRangeException(nameof(plaintext), "Resulting envelope exceeds configured maximum.");
            }

            byte[] envelope = new byte[total];
            int offset = 0;
            BinaryPrimitives.WriteUInt32BigEndian(envelope.AsSpan(offset, 4), Magic);
            offset += 4;
            envelope[offset++] = FormatVersion;
            BinaryPrimitives.WriteUInt16BigEndian(envelope.AsSpan(offset, 2), (ushort)keyIdBytes.Length);
            offset += 2;
            keyIdBytes.CopyTo(envelope.AsSpan(offset));
            offset += keyIdBytes.Length;
            nonce.CopyTo(envelope.AsSpan(offset));
            offset += NonceSize;
            tag.CopyTo(envelope.AsSpan(offset));
            offset += TagSize;
            ciphertext.CopyTo(envelope.AsSpan(offset));
            CryptographicOperations.ZeroMemory(nonce);
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(tag);
            return envelope;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>
    /// Decrypts an envelope produced by <see cref="Encrypt"/>.
    /// </summary>
    /// <param name="envelope">Complete envelope bytes.</param>
    /// <param name="associatedData">AAD that must match encryption AAD.</param>
    /// <returns>Newly allocated plaintext bytes.</returns>
    /// <exception cref="ArgumentException">Thrown when the envelope is malformed or oversized.</exception>
    /// <exception cref="AuthenticationTagMismatchException">Thrown when authentication fails. No plaintext is returned.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the embedded key cannot be resolved.</exception>
    public byte[] Decrypt(ReadOnlySpan<byte> envelope, ReadOnlySpan<byte> associatedData)
    {
        if (envelope.Length > _options.MaximumCiphertextBytes)
        {
            throw new ArgumentException("Envelope exceeds configured maximum size.", nameof(envelope));
        }

        if (envelope.Length < HeaderFixedSize)
        {
            throw new ArgumentException("Envelope is truncated.", nameof(envelope));
        }

        uint magic = BinaryPrimitives.ReadUInt32BigEndian(envelope[..4]);
        if (magic != Magic)
        {
            throw new ArgumentException("Envelope magic mismatch.", nameof(envelope));
        }

        byte version = envelope[4];
        if (version != FormatVersion)
        {
            throw new ArgumentException($"Unsupported envelope version '{version}'.", nameof(envelope));
        }

        ushort keyIdLength = BinaryPrimitives.ReadUInt16BigEndian(envelope.Slice(5, 2));
        int headerWithoutCipher = HeaderFixedSize + keyIdLength;
        if (envelope.Length < headerWithoutCipher)
        {
            throw new ArgumentException("Envelope is truncated after key identifier.", nameof(envelope));
        }

        string keyId = Encoding.UTF8.GetString(envelope.Slice(7, keyIdLength));
        ReadOnlySpan<byte> nonce = envelope.Slice(7 + keyIdLength, NonceSize);
        ReadOnlySpan<byte> tag = envelope.Slice(7 + keyIdLength + NonceSize, TagSize);
        ReadOnlySpan<byte> ciphertext = envelope[(7 + keyIdLength + NonceSize + TagSize)..];
        if (ciphertext.Length > _options.MaximumPlaintextBytes)
        {
            throw new ArgumentException("Ciphertext exceeds plaintext limit.", nameof(envelope));
        }

        if (!_resolver.TryResolve(keyId, out byte[] key))
        {
            throw new InvalidOperationException($"Key '{keyId}' could not be resolved.");
        }

        byte[] plaintext = new byte[ciphertext.Length];
        try
        {
            ValidateAes256Key(key);
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
            return plaintext;
        }
        catch (AuthenticationTagMismatchException)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>
    /// Reads envelope metadata without decrypting.
    /// </summary>
    /// <param name="envelope">Envelope bytes.</param>
    /// <returns>Parsed metadata including format version and key identifier.</returns>
    /// <exception cref="ArgumentException">Thrown when parsing fails or size limits are exceeded.</exception>
    public EnvelopeMetadata GetMetadata(ReadOnlySpan<byte> envelope)
    {
        if (envelope.Length > _options.MaximumCiphertextBytes || envelope.Length < HeaderFixedSize)
        {
            throw new ArgumentException("Envelope is invalid or oversized.", nameof(envelope));
        }

        if (BinaryPrimitives.ReadUInt32BigEndian(envelope[..4]) != Magic)
        {
            throw new ArgumentException("Envelope magic mismatch.", nameof(envelope));
        }

        byte version = envelope[4];
        ushort keyIdLength = BinaryPrimitives.ReadUInt16BigEndian(envelope.Slice(5, 2));
        if (envelope.Length < HeaderFixedSize + keyIdLength)
        {
            throw new ArgumentException("Envelope is truncated.", nameof(envelope));
        }

        string keyId = Encoding.UTF8.GetString(envelope.Slice(7, keyIdLength));
        int ciphertextLength = envelope.Length - HeaderFixedSize - keyIdLength;
        return new EnvelopeMetadata(version, keyId, ciphertextLength);
    }

    private static void ValidateAes256Key(byte[] key)
    {
        if (key.Length != 32)
        {
            throw new ArgumentException("AES-GCM envelope encryption requires a 256-bit key.", nameof(key));
        }
    }
}

/// <summary>
/// Metadata extracted from an encrypted envelope without decryption.
/// </summary>
/// <param name="FormatVersion">Envelope format version.</param>
/// <param name="KeyId">Key identifier used at encryption time.</param>
/// <param name="CiphertextLength">Length of the ciphertext portion in bytes.</param>
public sealed record EnvelopeMetadata(byte FormatVersion, string KeyId, int CiphertextLength);
