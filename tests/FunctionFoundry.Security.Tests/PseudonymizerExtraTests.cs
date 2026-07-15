using System.Security.Cryptography;

namespace FunctionFoundry.Security.Tests;

public sealed class PseudonymizerExtraTests
{
    [Fact]
    public void Base64Url_encoding_is_supported()
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);
        var p = new Pseudonymizer(key, new PseudonymizerOptions("c", "t", 1, PseudonymEncoding.Base64Url));
        string token = p.Pseudonymize("abc"u8);
        Assert.StartsWith("v1.", token, StringComparison.Ordinal);
        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);
        Assert.True(p.Verify(token, "abc"u8));
    }

    [Fact]
    public void Short_key_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new Pseudonymizer(new byte[8], new PseudonymizerOptions("c", "t")));
    }
}
