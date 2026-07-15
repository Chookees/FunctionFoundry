namespace FunctionFoundry.Observability.Tests;

public sealed class EventFingerprinterTests
{
    [Fact]
    public void Fingerprint_is_stable_when_volatile_tokens_change()
    {
        var fingerprinter = new EventFingerprinter();
        var baseline = new EventFingerprintInput(
            "order.failed",
            "Request failed for customer",
            Properties: new Dictionary<string, string> { ["region"] = "us-east" });

        string guid = Guid.NewGuid().ToString();
        var volatileInput = baseline with
        {
            Message = $"Request failed for customer {guid}",
        };

        EventFingerprint first = fingerprinter.Fingerprint(baseline);
        EventFingerprint second = fingerprinter.Fingerprint(volatileInput);

        Assert.Equal(first.Hash, second.Hash);
    }

    [Fact]
    public void Fingerprint_normalizes_exception_stack_lines_and_paths()
    {
        var fingerprinter = new EventFingerprinter();
        InvalidOperationException ex = CaptureException(static () => throw new InvalidOperationException("disk full at 0x7fff1234"));

        EventFingerprint fingerprint = fingerprinter.Fingerprint(new EventFingerprintInput(
            "worker.error",
            "execution failed",
            ex));

        FingerprintComponent stack = Assert.Single(fingerprint.Components, c => c.Name == "exception.chain");
        Assert.Contains("InvalidOperationException", stack.NormalizedValue, StringComparison.Ordinal);
        Assert.Contains("disk full", stack.NormalizedValue, StringComparison.Ordinal);
        Assert.DoesNotContain("0x7fff", stack.NormalizedValue, StringComparison.Ordinal);
        Assert.DoesNotContain(":line ", stack.NormalizedValue, StringComparison.OrdinalIgnoreCase);
    }

    private static InvalidOperationException CaptureException(Action action)
    {
        try
        {
            action();
            throw new InvalidOperationException("Expected exception was not thrown.");
        }
        catch (InvalidOperationException ex) when (ex.Message != "Expected exception was not thrown.")
        {
            return ex;
        }
    }

    [Fact]
    public void Fingerprint_differs_for_different_causal_messages()
    {
        var fingerprinter = new EventFingerprinter();
        EventFingerprint a = fingerprinter.Fingerprint(new EventFingerprintInput("evt", "timeout connecting"));
        EventFingerprint b = fingerprinter.Fingerprint(new EventFingerprintInput("evt", "permission denied"));
        Assert.NotEqual(a.Hash, b.Hash);
    }

    [Fact]
    public void Fingerprint_exposes_explainable_components()
    {
        var fingerprinter = new EventFingerprinter();
        EventFingerprint fingerprint = fingerprinter.Fingerprint(new EventFingerprintInput(
            "health",
            "degraded",
            Properties: new Dictionary<string, string> { ["dependency"] = "sql" }));

        Assert.True(fingerprint.Components.Count >= 3);
        Assert.All(fingerprint.Components, c => Assert.False(string.IsNullOrWhiteSpace(c.NormalizedValue)));
    }

    [Fact]
    public void Fingerprint_throws_for_null_input()
    {
        var fingerprinter = new EventFingerprinter();
        Assert.Throws<ArgumentNullException>(() => fingerprinter.Fingerprint(null!));
    }

    [Fact]
    public void Options_validate_rejects_negative_inner_exception_depth()
    {
        var options = new EventFingerprinterOptions { MaxInnerExceptionDepth = -1 };
        Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
    }
}
