using System.Security.Cryptography;
using System.Text;

namespace FunctionFoundry.Observability;

/// <summary>
/// Internal hashing helpers for observability primitives.
/// </summary>
internal static class ObservabilityHashing
{
    /// <summary>
    /// Computes a lowercase hexadecimal SHA-256 digest of <paramref name="value"/>.
    /// </summary>
    internal static string Sha256Hex(ReadOnlySpan<char> value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value.ToString());
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(bytes, hash);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// Computes a lowercase hexadecimal SHA-256 digest of UTF-8 bytes.
    /// </summary>
    internal static string Sha256Hex(ReadOnlySpan<byte> value)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(value, hash);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// Computes a deterministic 32-bit hash for stable sampling decisions.
    /// </summary>
    internal static uint StableHash32(ReadOnlySpan<char> value, uint seed)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;
        uint hash = seed ^ offsetBasis;
        foreach (char ch in value)
        {
            hash ^= ch;
            hash *= prime;
        }

        return hash;
    }
}
