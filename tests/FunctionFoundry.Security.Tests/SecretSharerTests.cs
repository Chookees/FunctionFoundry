using System.Security.Cryptography;

namespace FunctionFoundry.Security.Tests;

public sealed class SecretSharerTests
{
    [Fact]
    public void Split_and_combine_round_trips_with_threshold()
    {
        byte[] secret = RandomNumberGenerator.GetBytes(32);
        IReadOnlyList<SecretShare> shares = SecretSharer.Split(secret, threshold: 3, shareCount: 5);
        Assert.Equal(5, shares.Count);

        byte[] recovered = SecretSharer.Combine(shares.Take(3).ToArray());
        Assert.Equal(secret, recovered);

        ShareValidationResult duplicate = SecretSharer.ValidateShares([shares[0], shares[0], shares[1]]);
        Assert.False(duplicate.IsValid);
    }

    [Fact]
    public void Serialize_round_trip_preserves_share()
    {
        IReadOnlyList<SecretShare> shares = SecretSharer.Split([0x01, 0x02, 0x03], 2, 3);
        byte[] bytes = SecretSharer.SerializeShare(shares[0]);
        SecretShare parsed = SecretSharer.DeserializeShare(bytes);
        Assert.Equal(shares[0].X, parsed.X);
        Assert.True(shares[0].Y.Span.SequenceEqual(parsed.Y.Span));
    }

    [Fact]
    public void Deterministic_vectors_with_fixed_rng()
    {
        using var rng = new SequentialRng(seed: 7);
        IReadOnlyList<SecretShare> shares = SecretSharer.Split([0x42], threshold: 2, shareCount: 2, rng);
        byte[] recovered = SecretSharer.Combine(shares);
        Assert.Equal(new byte[] { 0x42 }, recovered);
    }

    private sealed class SequentialRng : RandomNumberGenerator
    {
        private byte _value;

        public SequentialRng(byte seed) => _value = seed;

        public override void GetBytes(byte[] data)
        {
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = _value++;
            }
        }
    }
}
