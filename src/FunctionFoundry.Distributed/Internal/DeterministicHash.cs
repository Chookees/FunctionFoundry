using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace FunctionFoundry.Distributed.Internal;

internal static class DeterministicHash
{
    public static ulong Hash64(ReadOnlySpan<byte> key, ReadOnlySpan<byte> nodeId)
    {
        Span<byte> buffer = stackalloc byte[key.Length + nodeId.Length + 1];
        key.CopyTo(buffer);
        buffer[key.Length] = 0;
        nodeId.CopyTo(buffer[(key.Length + 1)..]);

        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(buffer[..(key.Length + nodeId.Length + 1)], hash);
        return BinaryPrimitives.ReadUInt64LittleEndian(hash);
    }

    public static ulong Hash64(ReadOnlySpan<byte> key, string nodeId)
    {
        int byteCount = Encoding.UTF8.GetByteCount(nodeId);
        Span<byte> nodeBytes = byteCount <= 256 ? stackalloc byte[byteCount] : new byte[byteCount];
        _ = Encoding.UTF8.GetBytes(nodeId, nodeBytes);
        return Hash64(key, nodeBytes);
    }
}
