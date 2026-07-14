namespace FunctionFoundry.Security;

/// <summary>
/// Provides constant-time equality comparison helpers for secret values.
/// </summary>
public static class SecretComparer
{
    /// <summary>
    /// Compares two byte sequences in constant time relative to the longer input length.
    /// </summary>
    /// <param name="left">The first sequence.</param>
    /// <param name="right">The second sequence.</param>
    /// <returns><see langword="true"/> when both sequences have equal length and equal content; otherwise <see langword="false"/>.</returns>
    /// <remarks>
    /// Uses <see cref="System.Security.Cryptography.CryptographicOperations.FixedTimeEquals"/> when lengths match.
    /// Length mismatches return false after a fixed-time self-comparison to reduce obvious early-exit leakage.
    /// Thread safety: this method is thread-safe.
    /// </remarks>
    public static bool FixedTimeEquals(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        if (left.Length != right.Length)
        {
            _ = System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(left, left);
            return false;
        }

        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(left, right);
    }
}
