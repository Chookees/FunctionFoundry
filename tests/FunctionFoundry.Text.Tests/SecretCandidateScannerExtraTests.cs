namespace FunctionFoundry.Text.Tests;

public sealed class SecretCandidateScannerExtraTests
{
    [Fact]
    public void Empty_text_returns_no_findings()
    {
        Assert.Empty(new SecretCandidateScanner().Scan(string.Empty));
    }

    [Fact]
    public void Low_entropy_words_are_not_reported_as_secrets()
    {
        IReadOnlyList<SecretCandidateFinding> findings = new SecretCandidateScanner().Scan("the quick brown fox jumps over the lazy dog");
        Assert.DoesNotContain(findings, f => f.Confidence > 0.9);
    }
}
