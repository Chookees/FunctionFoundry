using System.Security.Cryptography;
using System.Text;
using FunctionFoundry.Storage;

namespace FunctionFoundry.Storage.Tests;

public sealed class ContentDefinedChunkerTests
{
    [Fact]
    public async Task ChunkAsync_empty_input_returns_empty_list()
    {
        var chunker = new ContentDefinedChunker();
        IReadOnlyList<ContentChunk> chunks = await chunker.ChunkAsync(ReadOnlyMemory<byte>.Empty);
        Assert.Empty(chunks);
    }

    [Fact]
    public async Task ChunkAsync_is_deterministic_for_same_input()
    {
        var chunker = new ContentDefinedChunker(new ContentDefinedChunkerOptions(1024, 4096, 8192, 32));
        byte[] payload = RandomNumberGenerator.GetBytes(64 * 1024);
        IReadOnlyList<ContentChunk> first = await chunker.ChunkAsync(payload);
        IReadOnlyList<ContentChunk> second = await chunker.ChunkAsync(payload);
        Assert.Equal(first.Count, second.Count);
        for (int i = 0; i < first.Count; i++)
        {
            Assert.Equal(first[i].Offset, second[i].Offset);
            Assert.Equal(first[i].Length, second[i].Length);
            Assert.Equal(first[i].Sha256Hex, second[i].Sha256Hex);
        }
    }

    [Fact]
    public async Task ChunkAsync_respects_maximum_chunk_size()
    {
        var chunker = new ContentDefinedChunker(new ContentDefinedChunkerOptions(512, 1024, 2048, 32));
        byte[] payload = RandomNumberGenerator.GetBytes(16 * 1024);
        IReadOnlyList<ContentChunk> chunks = await chunker.ChunkAsync(payload);
        Assert.All(chunks, chunk => Assert.InRange(chunk.Length, 1, 2048));
        Assert.Equal(payload.Length, chunks.Sum(static chunk => chunk.Length));
    }

    [Fact]
    public async Task ChunkAsync_hashes_match_payload_segments()
    {
        var chunker = new ContentDefinedChunker(new ContentDefinedChunkerOptions(256, 1024, 4096, 24));
        byte[] payload = Encoding.UTF8.GetBytes(new string('z', 12_000));
        IReadOnlyList<ContentChunk> chunks = await chunker.ChunkAsync(payload);
        foreach (ContentChunk chunk in chunks)
        {
            byte[] segment = payload.AsSpan((int)chunk.Offset, chunk.Length).ToArray();
            string expected = Convert.ToHexStringLower(SHA256.HashData(segment));
            Assert.Equal(expected, chunk.Sha256Hex);
        }
    }

    [Fact]
    public void Options_reject_invalid_sizes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ContentDefinedChunkerOptions(4096, 1024, 8192).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new ContentDefinedChunkerOptions(0, 1024, 8192).Validate());
    }

    [Fact]
    public async Task ChunkAsync_streaming_matches_buffer()
    {
        var chunker = new ContentDefinedChunker();
        byte[] payload = RandomNumberGenerator.GetBytes(32 * 1024);
        IReadOnlyList<ContentChunk> fromBuffer = await chunker.ChunkAsync(payload);
        await using var stream = new MemoryStream(payload);
        IReadOnlyList<ContentChunk> fromStream = await chunker.ChunkAsync(stream);
        Assert.Equal(fromBuffer.Count, fromStream.Count);
        for (int i = 0; i < fromBuffer.Count; i++)
        {
            Assert.Equal(fromBuffer[i].Sha256Hex, fromStream[i].Sha256Hex);
        }
    }
}
