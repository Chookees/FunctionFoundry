using System.Security.Cryptography;
using System.Text;

namespace FunctionFoundry.Security.Tests;

public sealed class EnvelopeEncryptorTests
{
    [Fact]
    public void Encrypt_decrypt_round_trips_with_aad()
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);
        var resolver = new DictionaryDataKeyResolver(new Dictionary<string, byte[]> { ["k1"] = key });
        var encryptor = new EnvelopeEncryptor(resolver);
        byte[] plaintext = Encoding.UTF8.GetBytes("payload");
        byte[] aad = Encoding.UTF8.GetBytes("tenant:a");

        byte[] envelope = encryptor.Encrypt("k1", plaintext, aad);
        byte[] roundTrip = encryptor.Decrypt(envelope, aad);

        Assert.Equal(plaintext, roundTrip);
        EnvelopeMetadata metadata = encryptor.GetMetadata(envelope);
        Assert.Equal("k1", metadata.KeyId);
        Assert.Equal((byte)1, metadata.FormatVersion);
    }

    [Fact]
    public void Decrypt_with_wrong_aad_does_not_return_plaintext()
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);
        var resolver = new DictionaryDataKeyResolver(new Dictionary<string, byte[]> { ["k1"] = key });
        var encryptor = new EnvelopeEncryptor(resolver);
        byte[] envelope = encryptor.Encrypt("k1", "secret"u8, "aad-a"u8);

        Assert.ThrowsAny<CryptographicException>(() => encryptor.Decrypt(envelope, "aad-b"u8));
    }

    [Fact]
    public void Tampered_ciphertext_fails_authentication()
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);
        var resolver = new DictionaryDataKeyResolver(new Dictionary<string, byte[]> { ["k1"] = key });
        var encryptor = new EnvelopeEncryptor(resolver);
        byte[] envelope = encryptor.Encrypt("k1", "secret"u8, ReadOnlySpan<byte>.Empty);
        envelope[^1] ^= 0x01;

        Assert.ThrowsAny<CryptographicException>(() => encryptor.Decrypt(envelope, ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void Truncated_and_oversized_envelopes_are_rejected()
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);
        var resolver = new DictionaryDataKeyResolver(new Dictionary<string, byte[]> { ["k1"] = key });
        var encryptor = new EnvelopeEncryptor(resolver, new EnvelopeEncryptionOptions(MaximumPlaintextBytes: 64, MaximumCiphertextBytes: 256));
        byte[] envelope = encryptor.Encrypt("k1", "ok"u8, ReadOnlySpan<byte>.Empty);

        Assert.Throws<ArgumentException>(() => encryptor.Decrypt(envelope.AsSpan(0, 8), ReadOnlySpan<byte>.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => encryptor.Encrypt("k1", new byte[128], ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void Unknown_key_id_fails_without_partial_plaintext()
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);
        var encryptResolver = new DictionaryDataKeyResolver(new Dictionary<string, byte[]> { ["k1"] = key });
        var decryptResolver = new DictionaryDataKeyResolver(new Dictionary<string, byte[]> { ["k2"] = RandomNumberGenerator.GetBytes(32) });
        byte[] envelope = new EnvelopeEncryptor(encryptResolver).Encrypt("k1", "x"u8, ReadOnlySpan<byte>.Empty);

        Assert.Throws<InvalidOperationException>(() => new EnvelopeEncryptor(decryptResolver).Decrypt(envelope, ReadOnlySpan<byte>.Empty));
    }
}
