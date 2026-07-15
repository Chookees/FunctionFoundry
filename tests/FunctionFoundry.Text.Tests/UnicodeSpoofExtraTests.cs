namespace FunctionFoundry.Text.Tests;

public sealed class UnicodeSpoofExtraTests
{
    [Fact]
    public void Empty_input_returns_no_findings()
    {
        UnicodeSpoofAnalysisResult result = new UnicodeSpoofDetector().Analyze(string.Empty);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Ascii_plain_text_has_no_spoof_findings()
    {
        UnicodeSpoofAnalysisResult result = new UnicodeSpoofDetector().Analyze("paypal");
        Assert.DoesNotContain(result.Findings, f => f.Kind == UnicodeSpoofRiskKind.ConfusableCharacter);
    }

    [Fact]
    public void Null_input_throws()
    {
        Assert.ThrowsAny<ArgumentNullException>(() => new UnicodeSpoofDetector().Analyze(null!));
    }
}
