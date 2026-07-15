using System.Text;

namespace FunctionFoundry.Integrity.Tests;

public sealed class HashChainTests
{
    [Fact]
    public void Append_and_verify_round_trip()
    {
        HashChainRecord genesis = HashChain.CreateGenesis("{\"event\":\"start\"}"u8);
        HashChainRecord second = HashChain.Append(genesis, "step-1"u8);
        HashChainRecord third = HashChain.Append(second, "step-2"u8);

        HashChainVerificationResult result = HashChain.Verify([genesis, second, third]);
        Assert.True(result.IsValid);
        Assert.Equal(third.RecordHashHex, result.HeadHashHex);
    }

    [Fact]
    public void Verification_reports_first_invalid_record_on_tamper()
    {
        HashChainRecord genesis = HashChain.CreateGenesis("g"u8);
        HashChainRecord second = HashChain.Append(genesis, "ok"u8);
        var tampered = second with { Payload = "bad"u8.ToArray() };

        HashChainVerificationResult result = HashChain.Verify([genesis, tampered]);
        Assert.False(result.IsValid);
        Assert.NotNull(result.FirstInvalidRecord);
        Assert.Equal(1, result.FirstInvalidRecord.Sequence);
        Assert.Contains("hash", result.FirstInvalidRecord.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Broken_previous_hash_is_detected()
    {
        HashChainRecord genesis = HashChain.CreateGenesis("g"u8);
        HashChainRecord second = HashChain.Append(genesis, "ok"u8);
        var broken = second with { PreviousHashHex = new string('0', 64) };

        HashChainVerificationResult result = HashChain.Verify([genesis, broken]);
        Assert.False(result.IsValid);
        Assert.Equal(1, result.FirstInvalidRecord?.Sequence);
    }

    [Fact]
    public void Timestamp_policy_required_enforced()
    {
        var options = new HashChainOptions(HashChainTimestampPolicy.Required);
        HashChainRecord genesis = HashChain.CreateGenesis("g"u8, options);
        Assert.Throws<ArgumentException>(() => HashChain.Append(genesis, "x"u8, options));

        DateTimeOffset ts = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        HashChainRecord second = HashChain.Append(genesis, "x"u8, options, ts);
        HashChainVerificationResult ok = HashChain.Verify([genesis, second], options);
        Assert.True(ok.IsValid);
    }

    [Fact]
    public void Json_payloads_are_canonicalized_before_hashing()
    {
        HashChainRecord fromUnsorted = HashChain.CreateGenesis("{\"b\":2,\"a\":1}"u8);
        HashChainRecord fromSorted = HashChain.CreateGenesis("{\"a\":1,\"b\":2}"u8);
        Assert.Equal(fromUnsorted.RecordHashHex, fromSorted.RecordHashHex);
        Assert.Equal(fromUnsorted.Payload, fromSorted.Payload);
    }

    [Fact]
    public void Serialize_record_body_is_canonical_json()
    {
        HashChainRecord record = HashChain.CreateGenesis("{\"z\":1,\"a\":2}"u8);
        byte[] body = HashChain.SerializeRecordBody(record);
        byte[] canonical = CanonicalJson.Canonicalize(body);
        Assert.Equal(canonical, body);
        Assert.DoesNotContain("recordHash", Encoding.UTF8.GetString(body), StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_empty_chain_is_invalid()
    {
        HashChainVerificationResult result = HashChain.Verify([]);
        Assert.False(result.IsValid);
        Assert.Equal(-1, result.FirstInvalidRecord?.Sequence);
        Assert.Contains("at least one", result.FirstInvalidRecord?.Reason ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}
