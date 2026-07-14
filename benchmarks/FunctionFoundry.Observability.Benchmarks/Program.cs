using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using FunctionFoundry.Observability;

namespace FunctionFoundry.Observability.Benchmarks;

internal static class Program
{
    private static void Main(string[] args) => _ = BenchmarkRunner.Run<ObservabilityBenchmarks>(args: args);
}

[MemoryDiagnoser]
public class ObservabilityBenchmarks
{
    private SensitiveDataRedactor _redactor = null!;
    private EventFingerprinter _fingerprinter = null!;
    private AdaptiveSampler _sampler = null!;
    private BurstCoalescer _coalescer = null!;
    private Dictionary<string, object?> _payload = null!;
    private EventFingerprintInput _fingerprintInput = null!;
    private string _fingerprint = null!;

    [GlobalSetup]
    public void Setup()
    {
        _redactor = new SensitiveDataRedactor();
        _fingerprinter = new EventFingerprinter();
        _sampler = new AdaptiveSampler();
        _coalescer = new BurstCoalescer();
        _payload = new Dictionary<string, object?>
        {
            ["user"] = "alice",
            ["password"] = "hunter2",
            ["items"] = Enumerable.Range(0, 32).Select(i => new Dictionary<string, object?> { ["id"] = i, ["token"] = $"tok-{i}-abcdefghijklmnop" }).ToArray(),
        };
        _fingerprintInput = new EventFingerprintInput("bench", "failure id=" + Guid.NewGuid());
        _fingerprint = _fingerprinter.Fingerprint(_fingerprintInput).Hash;
    }

    [Benchmark]
    public RedactionResult RedactPayload() => _redactor.Redact(_payload);

    [Benchmark]
    public EventFingerprint FingerprintEvent() => _fingerprinter.Fingerprint(_fingerprintInput);

    [Benchmark]
    public SamplingDecision AdaptiveSample() => _sampler.Decide(_fingerprint, EventSeverity.Information);

    [Benchmark]
    public void CoalesceBurst()
    {
        _coalescer.Record(_fingerprint, 1);
        _ = _coalescer.FlushAll();
    }
}
