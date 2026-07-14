using System.Security.Cryptography;

namespace FunctionFoundry.Security.Tests;

public sealed class KeyRotationPlannerTests
{
    [Fact]
    public void CreatePlan_lists_only_payloads_needing_rotation_in_deterministic_order()
    {
        byte[] key1 = RandomNumberGenerator.GetBytes(32);
        byte[] key2 = RandomNumberGenerator.GetBytes(32);
        var resolver = new DictionaryDataKeyResolver(new Dictionary<string, byte[]>
        {
            ["k1"] = key1,
            ["k2"] = key2,
        });
        var encryptor = new EnvelopeEncryptor(resolver);
        byte[] envA = encryptor.Encrypt("k1", "a"u8, ReadOnlySpan<byte>.Empty);
        byte[] envB = encryptor.Encrypt("k2", "b"u8, ReadOnlySpan<byte>.Empty);
        byte[] envC = encryptor.Encrypt("k1", "c"u8, ReadOnlySpan<byte>.Empty);

        var metadata = new Dictionary<string, EnvelopeMetadata>
        {
            ["payload-b"] = encryptor.GetMetadata(envB),
            ["payload-a"] = encryptor.GetMetadata(envA),
            ["payload-c"] = encryptor.GetMetadata(envC),
        };

        KeyRotationPlan plan = KeyRotationPlanner.CreatePlan(metadata, "k2");
        Assert.Equal(2, plan.Actions.Count);
        Assert.Equal(1, plan.AlreadyCurrentCount);
        Assert.Equal(["payload-a", "payload-c"], plan.Actions.Select(a => a.PayloadId).ToArray());
        Assert.All(plan.Actions, a => Assert.Equal("k2", a.TargetKeyId));
    }
}

public sealed class SecretComparerTests
{
    [Fact]
    public void FixedTimeEquals_matches_content_and_rejects_length_mismatch()
    {
        Assert.True(SecretComparer.FixedTimeEquals([1, 2, 3], [1, 2, 3]));
        Assert.False(SecretComparer.FixedTimeEquals([1, 2, 3], [1, 2, 4]));
        Assert.False(SecretComparer.FixedTimeEquals([1, 2], [1, 2, 3]));
    }
}
