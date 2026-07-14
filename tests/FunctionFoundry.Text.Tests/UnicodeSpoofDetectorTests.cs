namespace FunctionFoundry.Text.Tests;

public sealed class UnicodeSpoofDetectorTests
{
    [Fact]
    public void Detects_confusable_and_reports_version()
    {
        var detector = new UnicodeSpoofDetector();
        UnicodeSpoofAnalysisResult result = detector.Analyze("pаypal");

        Assert.Equal(ConfusablesData.Version, result.DataVersion);
        Assert.Contains(result.Findings, f => f.Kind == UnicodeSpoofRiskKind.ConfusableCharacter);
        Assert.Contains("PAYPAL", result.Skeleton, StringComparison.Ordinal);
    }

    [Fact]
    public void Detects_invisible_characters_without_intent_claims()
    {
        var detector = new UnicodeSpoofDetector();
        UnicodeSpoofAnalysisResult result = detector.Analyze("abc\u200Bdef");

        UnicodeSpoofFinding finding = Assert.Single(result.Findings, f => f.Kind == UnicodeSpoofRiskKind.InvisibleOrControl);
        Assert.DoesNotContain("malicious", finding.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Mixed_script_is_reported()
    {
        var detector = new UnicodeSpoofDetector();
        UnicodeSpoofAnalysisResult result = detector.Analyze("abcАД");

        Assert.Contains(result.Findings, f => f.Kind == UnicodeSpoofRiskKind.MixedScript);
    }
}
