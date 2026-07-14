using System.Security.Cryptography;

namespace FunctionFoundry.Storage.Internal;

internal static class Sha256Operations
{
    public static string ComputeHex(ReadOnlySpan<byte> data)
    {
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(data, hash);
        return Convert.ToHexStringLower(hash);
    }

    public static async Task<string> ComputeHexAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var sha = SHA256.Create();
        byte[] buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
        {
            sha.TransformBlock(buffer, 0, read, null, 0);
        }

        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexStringLower(sha.Hash ?? throw new InvalidOperationException("SHA-256 hash was not produced."));
    }

    public static async Task<(string Hex, long Size)> ComputeHexAndSizeAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var sha = SHA256.Create();
        byte[] buffer = new byte[81920];
        long size = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
        {
            size += read;
            sha.TransformBlock(buffer, 0, read, null, 0);
        }

        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return (Convert.ToHexStringLower(sha.Hash ?? throw new InvalidOperationException("SHA-256 hash was not produced.")), size);
    }

    public static void VerifyHexOrThrow(string expectedHex, ReadOnlySpan<byte> actual)
    {
        string actualHex = ComputeHex(actual);
        if (!string.Equals(expectedHex, actualHex, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Checksum mismatch. Expected {expectedHex}, actual {actualHex}.");
        }
    }

    public static byte[] ParseHex(string hex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hex);
        if (hex.Length != SHA256.HashSizeInBytes * 2)
        {
            throw new ArgumentException("SHA-256 hex strings must be 64 characters.", nameof(hex));
        }

        return Convert.FromHexString(hex);
    }

    public static string FormatObjectPath(string rootDirectory, string contentHashHex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHashHex);
        if (contentHashHex.Length != SHA256.HashSizeInBytes * 2)
        {
            throw new ArgumentException("SHA-256 hex strings must be 64 characters.", nameof(contentHashHex));
        }

        string prefix = contentHashHex[..2];
        string suffix = contentHashHex[2..];
        return Path.Combine(rootDirectory, prefix, suffix);
    }
}
