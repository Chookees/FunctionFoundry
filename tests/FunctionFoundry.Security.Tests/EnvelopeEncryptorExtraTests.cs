using System.Security.Cryptography;

namespace FunctionFoundry.Security.Tests;

public sealed class EnvelopeEncryptorExtraTests
{
    [Fact]
    public void Empty_plaintext_round_trips()
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);
        var encryptor = new EnvelopeEncryptor(new DictionaryDataKeyResolver(new Dictionary<string, byte[]> { ["k"] = key }));
        byte[] envelope = encryptor.Encrypt("k", ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty);
        byte[] plain = encryptor.Decrypt(envelope, ReadOnlySpan<byte>.Empty);
        Assert.Empty(plain);
    }

    [Fact]
    public void Wrong_key_length_is_rejected()
    {
        var resolver = new DictionaryDataKeyResolver(new Dictionary<string, byte[]> { ["k"] = new byte[16] });
        var encryptor = new EnvelopeEncryptor(resolver);
        Assert.ThrowsAny<ArgumentException>(() => encryptor.Encrypt("k", "x"u8, ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void Magic_mismatch_is_rejected()
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);
        var encryptor = new EnvelopeEncryptor(new DictionaryDataKeyResolver(new Dictionary<string, byte[]> { ["k"] = key }));
        byte[] envelope = encryptor.Encrypt("k", "x"u8, ReadOnlySpan<byte>.Empty);
        envelope[0] ^= 0xFF;
        Assert.Throws<ArgumentException>(() => encryptor.Decrypt(envelope, ReadOnlySpan<byte>.Empty));
    }
}
