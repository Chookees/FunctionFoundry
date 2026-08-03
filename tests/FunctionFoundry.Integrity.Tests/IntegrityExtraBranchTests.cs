using System.Text;
using FunctionFoundry.Integrity;

namespace FunctionFoundry.Integrity.Tests;

public sealed class IntegrityExtraBranchTests
{
    [Fact]
    public void CanonicalJson_rejects_empty_input_and_invalid_buffer_size()
    {
        Assert.Throws<ArgumentException>(() => CanonicalJson.Canonicalize(ReadOnlySpan<byte>.Empty));
        Assert.Throws<ArgumentException>(() => CanonicalJson.Canonicalize(string.Empty));
        using var input = new MemoryStream("{}"u8.ToArray());
        using var output = new MemoryStream();
        Assert.Throws<ArgumentOutOfRangeException>(() => CanonicalJson.Canonicalize(input, output, bufferSize: 0));
    }

    [Fact]
    public void CanonicalJson_TryCanonicalize_succeeds_for_valid_json()
    {
        bool ok = CanonicalJson.TryCanonicalize("{\"b\":1,\"a\":2}"u8, out byte[]? canonical, out string? error);
        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal("{\"a\":2,\"b\":1}", Encoding.UTF8.GetString(canonical!));
    }

    [Fact]
    public void CanonicalJson_nested_objects_and_arrays_sort_recursively()
    {
        const string Input = "{\"z\":[{\"b\":1,\"a\":2},true],\"a\":{\"y\":null,\"x\":false}}";
        string actual = Encoding.UTF8.GetString(CanonicalJson.Canonicalize(Input));
        Assert.Equal("{\"a\":{\"x\":false,\"y\":null},\"z\":[{\"a\":2,\"b\":1},true]}", actual);
    }

    [Fact]
    public void MerkleTree_rejects_empty_leaves_and_verifies_all_indices()
    {
        Assert.Throws<ArgumentException>(() => MerkleTree.Build(Array.Empty<byte[]>()));
        byte[][] leaves = ["a"u8.ToArray(), "b"u8.ToArray(), "c"u8.ToArray(), "d"u8.ToArray()];
        MerkleTree tree = MerkleTree.Build(leaves);
        for (int i = 0; i < leaves.Length; i++)
        {
            MerkleProof proof = tree.CreateProof(i);
            MerkleProofVerificationResult result = MerkleTree.VerifyProof(tree.Root.Span, leaves[i], proof);
            Assert.True(result.IsValid);
        }
    }

    [Fact]
    public void HashChain_rejects_null_records_and_detects_sequence_gaps()
    {
        Assert.Throws<ArgumentNullException>(() => HashChain.Verify(null!));
        HashChainRecord genesis = HashChain.CreateGenesis("g"u8);
        HashChainRecord second = HashChain.Append(genesis, "ok"u8);
        var gapped = second with { Sequence = 5 };
        HashChainVerificationResult result = HashChain.Verify([genesis, gapped]);
        Assert.False(result.IsValid);
        Assert.NotNull(result.FirstInvalidRecord);
    }

    [Fact]
    public void StreamingHashManifest_rejects_null_content_and_verifies_valid_set()
    {
        var builder = StreamingHashManifest.CreateBuilder();
        Stream? content = null;
        Assert.Throws<ArgumentNullException>(() => builder.AddEntry("x.txt", content!));
        builder.AddEntry("x.txt", new MemoryStream("payload"u8.ToArray()), includeSize: true);
        StreamingHashManifestDocument manifest = StreamingHashManifest.Parse(builder.Build());
        ManifestVerificationResult ok = StreamingHashManifest.Verify(
            manifest,
            new Dictionary<string, Stream> { ["x.txt"] = new MemoryStream("payload"u8.ToArray()) });
        Assert.True(ok.IsValid);
        Assert.Empty(ok.MissingPaths);
        Assert.Empty(ok.UnexpectedPaths);
        Assert.Empty(ok.HashMismatches);
    }
}
