using System.Security.Cryptography;
using System.Text;

namespace FunctionFoundry.Security.Tests;

public sealed class PseudonymizerTests
{
    [Fact]
    public void Pseudonymize_is_deterministic_and_domain_separated()
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);
        var a = new Pseudonymizer(key, new PseudonymizerOptions("orders", "tenant-a", 1));
        var b = new Pseudonymizer(key, new PseudonymizerOptions("orders", "tenant-b", 1));
        byte[] value = Encoding.UTF8.GetBytes("user-1");

        string token1 = a.Pseudonymize(value);
        string token2 = a.Pseudonymize(value);
        string otherTenant = b.Pseudonymize(value);

        Assert.Equal(token1, token2);
        Assert.NotEqual(token1, otherTenant);
        Assert.True(a.Verify(token1, value));
        Assert.False(a.Verify(token1, "user-2"u8));
    }

    [Fact]
    public void Migrate_requires_valid_previous_token()
    {
        byte[] oldKey = RandomNumberGenerator.GetBytes(32);
        byte[] newKey = RandomNumberGenerator.GetBytes(32);
        var previous = new Pseudonymizer(oldKey, new PseudonymizerOptions("orders", "t", 1));
        var next = new Pseudonymizer(newKey, new PseudonymizerOptions("orders", "t", 2));
        byte[] value = "id"u8.ToArray();
        string oldToken = previous.Pseudonymize(value);

        string migrated = next.Migrate(previous, oldToken, value);
        Assert.StartsWith("v2.", migrated, StringComparison.Ordinal);
        Assert.True(next.Verify(migrated, value));
        Assert.Throws<InvalidOperationException>(() => next.Migrate(previous, "v1.deadbeef", value));
    }
}
