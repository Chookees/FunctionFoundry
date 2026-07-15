namespace FunctionFoundry.Observability.Tests;

public sealed class AdaptiveSamplerTests
{
    [Fact]
    public void Decide_always_samples_high_severity()
    {
        var sampler = new AdaptiveSampler(new AdaptiveSamplerOptions { BaseSampleRate = 0.0, MinSampleRate = 0.0 });
        SamplingDecision decision = sampler.Decide("fp-error", EventSeverity.Error);
        Assert.True(decision.ShouldSample);
        Assert.Equal("severity-bypass", decision.Reason);
    }

    [Fact]
    public void Decide_is_stable_for_identical_fingerprints()
    {
        var sampler = new AdaptiveSampler(new AdaptiveSamplerOptions
        {
            BaseSampleRate = 0.4,
            MinSampleRate = 0.4,
            AdaptationStartVolume = 10_000,
            DecisionSeed = 42,
        });

        SamplingDecision first = sampler.Decide("fp-stable", EventSeverity.Information);
        for (int i = 0; i < 50; i++)
        {
            SamplingDecision repeat = sampler.Decide("fp-stable", EventSeverity.Information);
            Assert.Equal(first.ShouldSample, repeat.ShouldSample);
            Assert.Equal(first.EffectiveRate, repeat.EffectiveRate);
        }
    }

    [Fact]
    public void Decide_adapts_rate_as_volume_grows()
    {
        var sampler = new AdaptiveSampler(new AdaptiveSamplerOptions
        {
            BaseSampleRate = 1.0,
            MinSampleRate = 0.05,
            AdaptationStartVolume = 10,
            AlwaysSampleFromSeverity = EventSeverity.Fatal,
        });

        for (int i = 0; i < 200; i++)
        {
            sampler.Decide($"fp-{i % 5}", EventSeverity.Information);
        }

        AdaptiveSamplerStats stats = sampler.Stats;
        Assert.True(stats.CurrentGlobalRate < 1.0);
        Assert.True(stats.CurrentGlobalRate >= 0.05);
        Assert.Equal(200, stats.TotalDecisions);
    }

    [Fact]
    public void Decide_respects_fingerprint_bound()
    {
        var sampler = new AdaptiveSampler(new AdaptiveSamplerOptions
        {
            MaxTrackedFingerprints = 16,
            AdaptationStartVolume = 10_000,
        });

        for (int i = 0; i < 64; i++)
        {
            sampler.Decide($"fp-{i}", EventSeverity.Verbose);
        }

        Assert.Equal(16, sampler.Stats.TrackedFingerprintCount);
    }

    [Fact]
    public void Decide_supports_concurrent_calls()
    {
        var sampler = new AdaptiveSampler();
        Parallel.For(0, 128, i => sampler.Decide($"fp-{i % 8}", EventSeverity.Warning));
        AdaptiveSamplerStats stats = sampler.Stats;
        Assert.Equal(128, stats.TotalDecisions);
        Assert.Equal(stats.SampledCount + stats.DroppedCount, stats.TotalDecisions);
    }

    [Fact]
    public void Options_validate_rejects_invalid_sample_rates()
    {
        var options = new AdaptiveSamplerOptions { BaseSampleRate = 0.2, MinSampleRate = 0.5 };
        Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
    }
}
