using System.Security.Cryptography;
using System.Text;
using FunctionFoundry.Networking;

namespace FunctionFoundry.Networking.Tests;

public sealed class StreamingIntegrityVerifierTests
{
    [Fact]
    public void Incremental_chunk_and_full_verification_succeed()
    {
        byte[] chunk0 = "hello"u8.ToArray();
        byte[] chunk1 = "world"u8.ToArray();
        byte[] full = Encoding.UTF8.GetBytes("helloworld");
        string expected = Convert.ToHexString(SHA256.HashData(full)).ToUpperInvariant();

        using var verifier = new StreamingIntegrityVerifier();
        verifier.AppendChunk(0, chunk0);
        verifier.AppendChunk(1, chunk1);
        ChunkVerificationResult chunkResult = verifier.VerifyChunk(0);
        Assert.False(string.IsNullOrEmpty(chunkResult.ActualHashHex));
        FullVerificationResult fullResult = verifier.VerifyFull(expected);
        Assert.True(fullResult.IsValid);
    }

    [Fact]
    public void Chunk_mismatch_reports_location()
    {
        using var verifier = new StreamingIntegrityVerifier();
        verifier.SetExpectedChunkHash(0, "00");
        verifier.AppendChunk(0, "bad"u8.ToArray());
        ChunkVerificationResult chunk = verifier.VerifyChunk(0);
        Assert.False(chunk.IsValid);
        FullVerificationResult full = verifier.VerifyFull("ff");
        Assert.Equal(0, full.FirstMismatchChunkIndex);
    }
}
