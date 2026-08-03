using System.Security.Cryptography;
using FunctionFoundry.Security;

namespace FunctionFoundry.Security.Tests;

public sealed class HkdfDataKeyDeriverTests
{
    [Fact]
    public void DeriveAes256Key_is_deterministic_for_same_inputs()
    {
        var deriver = new HkdfDataKeyDeriver();
        byte[] master = RandomNumberGenerator.GetBytes(32);
        byte[] salt = "salt"u8.ToArray();
        byte[] info = "envelope-v1"u8.ToArray();

        byte[] first = deriver.DeriveAes256Key(master, salt, info);
        byte[] second = deriver.DeriveAes256Key(master, salt, info);

        Assert.Equal(32, first.Length);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Different_info_produces_different_keys()
    {
        var deriver = new HkdfDataKeyDeriver();
        byte[] master = RandomNumberGenerator.GetBytes(32);
        byte[] left = deriver.DeriveAes256Key(master, ReadOnlySpan<byte>.Empty, "a"u8);
        byte[] right = deriver.DeriveAes256Key(master, ReadOnlySpan<byte>.Empty, "b"u8);
        Assert.NotEqual(left, right);
    }

    [Fact]
    public void Rejects_empty_master_and_invalid_lengths()
    {
        var deriver = new HkdfDataKeyDeriver();
        Assert.Throws<ArgumentException>(() => deriver.DeriveBytes(ReadOnlySpan<byte>.Empty, 32, default, default));
        Assert.Throws<ArgumentOutOfRangeException>(() => deriver.DeriveBytes(RandomNumberGenerator.GetBytes(16), 8, default, default));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HkdfDataKeyDeriverOptions(32, 16).Validate());
    }
}
