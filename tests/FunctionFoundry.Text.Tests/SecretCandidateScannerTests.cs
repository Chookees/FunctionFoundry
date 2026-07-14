using System.Text;

namespace FunctionFoundry.Text.Tests;

public sealed class SecretCandidateScannerTests
{
    [Fact]
    public void Pattern_match_is_redacted()
    {
        var scanner = new SecretCandidateScanner();
        const string input = "aws_access_key_id=AKIA1234567890ABCDEF";
        SecretCandidateFinding finding = Assert.Single(scanner.Scan(input), f => f.Kind == SecretCandidateKind.PatternMatch);

        Assert.Contains('…', finding.RedactedPreview);
        Assert.DoesNotContain("AKIA1234567890ABCDEF", finding.RedactedPreview, StringComparison.Ordinal);
        Assert.Equal(1, finding.Line);
    }

    [Fact]
    public void High_entropy_token_reports_line_and_column()
    {
        var scanner = new SecretCandidateScanner(new SecretCandidateScannerOptions(MinimumTokenLength: 20));
        const string token = "Zx9_kLmN0pQrStUvWxYzAbCdEfGh";
        SecretCandidateFinding finding = Assert.Single(scanner.Scan($"prefix {token} suffix"), f => f.Kind == SecretCandidateKind.HighEntropyToken);
        Assert.True(finding.Confidence > 0);
        Assert.Equal(8, finding.Column);
    }

    [Fact]
    public async Task Streaming_scan_reads_lines()
    {
        var scanner = new SecretCandidateScanner();
        using MemoryStream stream = new(Encoding.UTF8.GetBytes("line1\nAKIA1234567890ABCD\n"));
        IReadOnlyList<SecretCandidateFinding> findings = await scanner.ScanAsync(stream);
        Assert.Contains(findings, f => f.Line == 2);
    }

    [Fact]
    public void Example_tokens_are_suppressed()
    {
        var scanner = new SecretCandidateScanner();
        Assert.Empty(scanner.Scan("token=example_api_key_value_12345678"));
    }
}
