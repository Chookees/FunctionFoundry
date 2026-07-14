using System.Text;

namespace FunctionFoundry.Integrity.Tests;

public sealed class CanonicalJsonTests
{
    [Theory]
    [InlineData("{\"b\":2,\"a\":1}", "{\"a\":1,\"b\":2}")]
    [InlineData("{\"z\":true,\"a\":null}", "{\"a\":null,\"z\":true}")]
    [InlineData("[3,1,2]", "[3,1,2]")]
    [InlineData("{\"n\":1.2300}", "{\"n\":1.23}")]
    [InlineData("{\"n\":0}", "{\"n\":0}")]
    [InlineData("{\"s\":\"\\u0041\"}", "{\"s\":\"A\"}")]
    public void Conformance_vectors_produce_expected_canonical_form(string input, string expected)
    {
        byte[] canonical = CanonicalJson.Canonicalize(input);
        Assert.Equal(expected, Encoding.UTF8.GetString(canonical));
    }

    [Fact]
    public void Unicode_and_control_characters_use_profile_escaping()
    {
        const string Input = "{\"msg\":\"\\u0009tab\"}";
        byte[] canonical = CanonicalJson.Canonicalize(Input);
        Assert.Equal("{\"msg\":\"\\u0009tab\"}", Encoding.UTF8.GetString(canonical));
    }

    [Fact]
    public void Duplicate_property_names_are_rejected()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(() => CanonicalJson.Canonicalize("{\"a\":1,\"a\":2}"));
        Assert.Contains("Duplicate", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Non_finite_numbers_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => CanonicalJson.Canonicalize("NaN"));
        Assert.Throws<ArgumentException>(() => CanonicalJson.Canonicalize("{\"x\":NaN}"));
    }

    [Fact]
    public void Leading_zero_numbers_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => CanonicalJson.Canonicalize("{\"x\":01}"));
    }

    [Fact]
    public void Utf8_bom_is_rejected()
    {
        byte[] bomJson = [0xEF, 0xBB, 0xBF, (byte)'{', (byte)'}'];
        Assert.Throws<ArgumentException>(() => CanonicalJson.Canonicalize(bomJson));
    }

    [Fact]
    public void Streaming_canonicalization_matches_in_memory()
    {
        const string Json = "{\"b\":2,\"a\":1}";
        byte[] expected = CanonicalJson.Canonicalize(Json);

        using var input = new MemoryStream(Encoding.UTF8.GetBytes(Json));
        using var output = new MemoryStream();
        CanonicalJson.Canonicalize(input, output);

        Assert.Equal(expected, output.ToArray());
    }

    [Fact]
    public void TryCanonicalize_returns_error_without_throwing()
    {
        bool ok = CanonicalJson.TryCanonicalize("{bad"u8, out byte[]? canonical, out string? error);
        Assert.False(ok);
        Assert.Null(canonical);
        Assert.NotNull(error);
    }

    [Fact]
    public void Profile_id_is_documented_constant()
    {
        Assert.Equal("FunctionFoundry.CanonicalJson/v1", CanonicalJson.ProfileId);
    }
}
