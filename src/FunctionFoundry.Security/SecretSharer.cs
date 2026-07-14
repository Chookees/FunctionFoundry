using System.Buffers.Binary;
using System.Security.Cryptography;

namespace FunctionFoundry.Security;

/// <summary>
/// A single Shamir secret share.
/// </summary>
public sealed class SecretShare
{
    private readonly byte[] _y;

    /// <summary>
    /// Initializes a new instance of the <see cref="SecretShare"/> class.
    /// </summary>
    /// <param name="version">Share format version.</param>
    /// <param name="threshold">Reconstruction threshold encoded with the share.</param>
    /// <param name="shareCount">Total shares issued in the sharing operation.</param>
    /// <param name="x">Share X coordinate in 1..255.</param>
    /// <param name="y">Share Y bytes; copied. Length equals secret length.</param>
    public SecretShare(byte version, byte threshold, byte shareCount, byte x, byte[] y)
    {
        ArgumentNullException.ThrowIfNull(y);
        Version = version;
        Threshold = threshold;
        ShareCount = shareCount;
        X = x;
        _y = y.ToArray();
    }

    /// <summary>Gets the share format version.</summary>
    public byte Version { get; }

    /// <summary>Gets the reconstruction threshold encoded with the share.</summary>
    public byte Threshold { get; }

    /// <summary>Gets the total shares issued in the sharing operation.</summary>
    public byte ShareCount { get; }

    /// <summary>Gets the share X coordinate in 1..255.</summary>
    public byte X { get; }

    /// <summary>Gets the share Y bytes as read-only memory; length equals secret length.</summary>
    public ReadOnlyMemory<byte> Y => _y;

    internal ReadOnlySpan<byte> YSpan => _y;
}

/// <summary>
/// Result of validating a set of shares before reconstruction.
/// </summary>
/// <param name="IsValid">Whether the share set is structurally valid.</param>
/// <param name="Errors">Human-readable validation errors; empty when valid.</param>
public sealed record ShareValidationResult(bool IsValid, IReadOnlyList<string> Errors);

/// <summary>
/// Implements Shamir's secret sharing over GF(256).
/// </summary>
/// <remarks>
/// <para>Algorithm reference: Adi Shamir, "How to Share a Secret", Communications of the ACM, 1979.
/// Field arithmetic uses AES GF(2^8) polynomial 0x11B.</para>
/// <para>Each secret byte is shared independently with a degree (t-1) polynomial.</para>
/// <para>Corruption detection: structural duplicates and version mismatches are rejected. Reconstructing with
/// inconsistent shares yields a mathematically interpolated value that will not match an authenticated secret
/// unless callers compare against a known digest.</para>
/// <para>Thread safety: static API is thread-safe.</para>
/// <para>Determinism: given the same secret, threshold, share count, and RNG outputs, share Y values are deterministic.</para>
/// </remarks>
public static class SecretSharer
{
    private const byte FormatVersion = 1;

    /// <summary>
    /// Splits a secret into <paramref name="shareCount"/> shares with the given threshold.
    /// </summary>
    /// <param name="secret">Secret bytes. Maximum length 65535. Ownership remains with the caller.</param>
    /// <param name="threshold">Minimum shares required to reconstruct. Must be in [2, shareCount].</param>
    /// <param name="shareCount">Total shares to create. Must be in [2, 255].</param>
    /// <param name="rng">Optional random number generator. Defaults to <see cref="RandomNumberGenerator"/>.</param>
    /// <returns>Read-only list of shares with X coordinates 1..shareCount.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when parameters are outside supported ranges.</exception>
    /// <example>
    /// <code>
    /// IReadOnlyList&lt;SecretShare&gt; shares = SecretSharer.Split(secret, threshold: 3, shareCount: 5);
    /// byte[] recovered = SecretSharer.Combine(shares.Take(3).ToArray());
    /// </code>
    /// </example>
    public static IReadOnlyList<SecretShare> Split(
        ReadOnlySpan<byte> secret,
        int threshold,
        int shareCount,
        RandomNumberGenerator? rng = null)
    {
        if (secret.Length == 0 || secret.Length > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(secret), "Secret length must be in 1..65535.");
        }

        if (shareCount is < 2 or > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(shareCount));
        }

        if (threshold < 2 || threshold > shareCount)
        {
            throw new ArgumentOutOfRangeException(nameof(threshold));
        }

        if (rng is null)
        {
            using RandomNumberGenerator owned = RandomNumberGenerator.Create();
            return SplitCore(secret, threshold, shareCount, owned);
        }

        return SplitCore(secret, threshold, shareCount, rng);
    }

    private static SecretShare[] SplitCore(
        ReadOnlySpan<byte> secret,
        int threshold,
        int shareCount,
        RandomNumberGenerator activeRng)
    {
        var yBuffers = new byte[shareCount][];
        for (int i = 0; i < shareCount; i++)
        {
            yBuffers[i] = new byte[secret.Length];
        }

        Span<byte> coefficients = threshold <= 64 ? stackalloc byte[threshold] : new byte[threshold];
        for (int byteIndex = 0; byteIndex < secret.Length; byteIndex++)
        {
            coefficients[0] = secret[byteIndex];
            activeRng.GetBytes(coefficients[1..]);
            for (int shareIndex = 0; shareIndex < shareCount; shareIndex++)
            {
                byte x = (byte)(shareIndex + 1);
                yBuffers[shareIndex][byteIndex] = EvaluatePolynomial(coefficients, x);
            }
        }

        if (coefficients.Length > 64)
        {
            CryptographicOperations.ZeroMemory(coefficients);
        }
        else
        {
            coefficients.Clear();
        }

        var shares = new SecretShare[shareCount];
        for (int i = 0; i < shareCount; i++)
        {
            shares[i] = new SecretShare(FormatVersion, (byte)threshold, (byte)shareCount, (byte)(i + 1), yBuffers[i]);
            CryptographicOperations.ZeroMemory(yBuffers[i]);
        }

        return shares;
    }

    /// <summary>
    /// Validates share structure, versions, duplicate X values, and consistent metadata.
    /// </summary>
    /// <param name="shares">Candidate shares.</param>
    /// <returns>Validation result with error messages when invalid.</returns>
    public static ShareValidationResult ValidateShares(IReadOnlyList<SecretShare> shares)
    {
        ArgumentNullException.ThrowIfNull(shares);
        var errors = new List<string>();
        if (shares.Count == 0)
        {
            errors.Add("At least one share is required.");
            return new ShareValidationResult(false, errors);
        }

        var seenX = new HashSet<byte>();
        SecretShare first = shares[0];
        foreach (SecretShare share in shares)
        {
            if (share.Version != FormatVersion)
            {
                errors.Add($"Unsupported share version {share.Version}.");
            }

            if (share.X == 0)
            {
                errors.Add("Share X coordinate must be non-zero.");
            }

            if (!seenX.Add(share.X))
            {
                errors.Add($"Duplicate share X coordinate {share.X}.");
            }

            if (share.Threshold != first.Threshold || share.ShareCount != first.ShareCount)
            {
                errors.Add("Share metadata mismatch across the provided set.");
            }

            if (share.Y.Length != first.Y.Length)
            {
                errors.Add("Share Y lengths are inconsistent.");
            }
        }

        if (shares.Count < first.Threshold)
        {
            errors.Add($"At least {first.Threshold} shares are required.");
        }

        return new ShareValidationResult(errors.Count == 0, errors);
    }

    /// <summary>
    /// Reconstructs a secret from validated shares using Lagrange interpolation at x=0.
    /// </summary>
    /// <param name="shares">At least threshold distinct shares.</param>
    /// <returns>Reconstructed secret bytes.</returns>
    /// <exception cref="ArgumentException">Thrown when validation fails.</exception>
    public static byte[] Combine(IReadOnlyList<SecretShare> shares)
    {
        ShareValidationResult validation = ValidateShares(shares);
        if (!validation.IsValid)
        {
            throw new ArgumentException(string.Join(" ", validation.Errors), nameof(shares));
        }

        SecretShare[] selected = shares.OrderBy(s => s.X).Take(shares[0].Threshold).ToArray();
        byte[] secret = new byte[selected[0].Y.Length];
        for (int byteIndex = 0; byteIndex < secret.Length; byteIndex++)
        {
            secret[byteIndex] = Interpolate(selected, byteIndex);
        }

        return secret;
    }

    private static byte Interpolate(SecretShare[] shares, int byteIndex)
    {
        byte result = 0;
        for (int i = 0; i < shares.Length; i++)
        {
            byte numerator = 1;
            byte denominator = 1;
            for (int j = 0; j < shares.Length; j++)
            {
                if (i == j)
                {
                    continue;
                }

                numerator = GfMul(numerator, shares[j].X);
                denominator = GfMul(denominator, (byte)(shares[i].X ^ shares[j].X));
            }

            byte lagrange = GfMul(numerator, GfInv(denominator));
            result = (byte)(result ^ GfMul(shares[i].YSpan[byteIndex], lagrange));
        }

        return result;
    }

    private static byte EvaluatePolynomial(ReadOnlySpan<byte> coefficients, byte x)
    {
        byte result = 0;
        for (int i = coefficients.Length - 1; i >= 0; i--)
        {
            result = (byte)(GfMul(result, x) ^ coefficients[i]);
        }

        return result;
    }

    private static byte GfMul(byte a, byte b)
    {
        byte product = 0;
        for (int i = 0; i < 8; i++)
        {
            if ((b & 1) != 0)
            {
                product ^= a;
            }

            bool hi = (a & 0x80) != 0;
            a <<= 1;
            if (hi)
            {
                a ^= 0x1B;
            }

            b >>= 1;
        }

        return product;
    }

    private static byte GfInv(byte a)
    {
        if (a == 0)
        {
            throw new InvalidOperationException("Cannot invert zero in GF(256).");
        }

        byte result = 1;
        byte baseValue = a;
        int exponent = 254;
        while (exponent > 0)
        {
            if ((exponent & 1) != 0)
            {
                result = GfMul(result, baseValue);
            }

            baseValue = GfMul(baseValue, baseValue);
            exponent >>= 1;
        }

        return result;
    }

    /// <summary>
    /// Serializes a share to a versioned binary representation.
    /// </summary>
    /// <param name="share">Share to serialize.</param>
    /// <returns>Binary encoding.</returns>
    public static byte[] SerializeShare(SecretShare share)
    {
        ArgumentNullException.ThrowIfNull(share);
        byte[] buffer = new byte[1 + 1 + 1 + 1 + 2 + share.Y.Length];
        buffer[0] = share.Version;
        buffer[1] = share.Threshold;
        buffer[2] = share.ShareCount;
        buffer[3] = share.X;
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(4, 2), (ushort)share.Y.Length);
        share.YSpan.CopyTo(buffer.AsSpan(6));
        return buffer;
    }

    /// <summary>
    /// Deserializes a share produced by <see cref="SerializeShare"/>.
    /// </summary>
    /// <param name="data">Serialized share bytes.</param>
    /// <returns>Parsed share.</returns>
    /// <exception cref="ArgumentException">Thrown when data is malformed.</exception>
    public static SecretShare DeserializeShare(ReadOnlySpan<byte> data)
    {
        if (data.Length < 6)
        {
            throw new ArgumentException("Share payload is truncated.", nameof(data));
        }

        byte version = data[0];
        if (version != FormatVersion)
        {
            throw new ArgumentException($"Unsupported share version {version}.", nameof(data));
        }

        ushort yLength = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(4, 2));
        if (data.Length != 6 + yLength)
        {
            throw new ArgumentException("Share payload length mismatch.", nameof(data));
        }

        return new SecretShare(version, data[1], data[2], data[3], data.Slice(6, yLength).ToArray());
    }
}
