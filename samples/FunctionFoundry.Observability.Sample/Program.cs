using FunctionFoundry.Observability;

var redactor = new SensitiveDataRedactor();
var payload = new Dictionary<string, object?>
{
    ["user"] = "alice",
    ["password"] = "hunter2",
    ["metadata"] = new Dictionary<string, object?> { ["authorization"] = "Bearer secret.token.value" },
};
RedactionResult redacted = redactor.Redact(payload);
Console.WriteLine($"Redacted fields: {redacted.RedactedFieldCount}");

var fingerprinter = new EventFingerprinter();
EventFingerprint fingerprint = fingerprinter.Fingerprint(new EventFingerprintInput(
    "payment.failed",
    $"Card declined id={Guid.NewGuid()} at {DateTimeOffset.UtcNow:O}"));
Console.WriteLine($"Fingerprint: {fingerprint.Hash[..16]}... ({fingerprint.Components.Count} components)");

var sampler = new AdaptiveSampler(new AdaptiveSamplerOptions { BaseSampleRate = 0.5, DecisionSeed = 7 });
SamplingDecision decision = sampler.Decide(fingerprint.Hash, EventSeverity.Warning);
Console.WriteLine($"Sample decision: {decision.ShouldSample} ({decision.Reason})");

var coalescer = new BurstCoalescer(new BurstCoalescerOptions { Window = TimeSpan.FromSeconds(1), MaxSamplesPerBurst = 2 });
for (int i = 0; i < 5; i++)
{
    coalescer.Record(fingerprint.Hash, new { attempt = i });
}

BurstSummary burst = coalescer.FlushAll().Single();
Console.WriteLine($"Burst count={burst.TotalCount}, dropped={burst.DroppedCount}, samples={burst.Samples.Count}");
