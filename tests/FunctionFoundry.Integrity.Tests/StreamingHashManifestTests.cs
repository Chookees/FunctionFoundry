using System.Security.Cryptography;
using System.Text;

namespace FunctionFoundry.Integrity.Tests;

public sealed class StreamingHashManifestTests
{
    [Fact]
    public void Builder_produces_deterministic_manifest_for_streams()
    {
        var builder = StreamingHashManifest.CreateBuilder();
        using var alpha = new MemoryStream("alpha"u8.ToArray());
        using var beta = new MemoryStream("beta-content"u8.ToArray());
        builder.AddEntry("dir/alpha.txt", alpha);
        builder.AddEntry("beta.txt", beta);

        byte[] first = builder.Build();
        byte[] second = builder.Build();
        Assert.Equal(first, second);
        Assert.StartsWith("{\"entries\"", Encoding.UTF8.GetString(first), StringComparison.Ordinal);
    }

    [Fact]
    public void Sha384_manifest_round_trips()
    {
        var builder = StreamingHashManifest.CreateBuilder(ManifestHashAlgorithm.Sha384);
        builder.AddEntry("x.bin", new MemoryStream([1, 2, 3]), includeSize: true);
        byte[] manifest = builder.Build();
        StreamingHashManifestDocument parsed = StreamingHashManifest.Parse(manifest);
        Assert.Equal(ManifestHashAlgorithm.Sha384, parsed.Algorithm);
        Assert.Single(parsed.Entries);
        Assert.Equal(96, parsed.Entries[0].HashHex.Length);
    }

    [Fact]
    public void Verify_reports_hash_and_missing_paths()
    {
        var builder = StreamingHashManifest.CreateBuilder();
        builder.AddEntry("ok.txt", new MemoryStream("ok"u8.ToArray()));
        builder.AddEntry("missing.txt", new MemoryStream("gone"u8.ToArray()));
        StreamingHashManifestDocument manifest = StreamingHashManifest.Parse(builder.Build());

        var entries = new Dictionary<string, Stream>
        {
            ["ok.txt"] = new MemoryStream("ok"u8.ToArray()),
            ["extra.txt"] = new MemoryStream("extra"u8.ToArray()),
        };

        ManifestVerificationResult result = StreamingHashManifest.Verify(manifest, entries);
        Assert.False(result.IsValid);
        Assert.Contains("missing.txt", result.MissingPaths);
        Assert.Contains("extra.txt", result.UnexpectedPaths);
    }

    [Fact]
    public void Verify_reports_hash_mismatch()
    {
        var builder = StreamingHashManifest.CreateBuilder();
        builder.AddEntry("file.txt", new MemoryStream("expected"u8.ToArray()));
        StreamingHashManifestDocument manifest = StreamingHashManifest.Parse(builder.Build());

        var entries = new Dictionary<string, Stream>
        {
            ["file.txt"] = new MemoryStream("tampered"u8.ToArray()),
        };

        ManifestVerificationResult result = StreamingHashManifest.Verify(manifest, entries);
        Assert.False(result.IsValid);
        Assert.Single(result.HashMismatches);
        Assert.Equal("file.txt", result.HashMismatches[0].RelativePath);
    }

    [Theory]
    [InlineData("../secret")]
    [InlineData("/abs")]
    [InlineData("C:\\abs")]
    [InlineData("a/../b")]
    [InlineData("a/./b")]
    public void Path_traversal_and_absolute_paths_are_rejected(string unsafePath)
    {
        var builder = StreamingHashManifest.CreateBuilder();
        Assert.Throws<ArgumentException>(() => builder.AddEntry(unsafePath, new MemoryStream([1])));
    }

    [Fact]
    public void Metadata_policy_rejects_disallowed_custom_metadata()
    {
        var builder = StreamingHashManifest.CreateBuilder(metadataPolicy: new ManifestMetadataPolicy(AllowCustomMetadata: true, AllowedCustomMetadataKeys: new HashSet<string> { "owner" }));
        builder.AddMetadata("owner", "team-a");
        Assert.Throws<ArgumentException>(() => builder.AddMetadata("extra", "nope"));

        byte[] manifest = builder.Build();
        var strict = new ManifestMetadataPolicy(AllowCustomMetadata: false);
        Assert.Throws<ArgumentException>(() => StreamingHashManifest.Parse(manifest, strict));
    }

    [Fact]
    public void HashStream_matches_incremental_sha256()
    {
        byte[] data = Encoding.UTF8.GetBytes("manifest-stream");
        using var stream = new MemoryStream(data);
        (string hashHex, long size) = StreamingHashManifest.HashStream(stream);
        string expected = Convert.ToHexStringLower(SHA256.HashData(data));
        Assert.Equal(expected, hashHex);
        Assert.Equal(data.Length, size);
    }
}
