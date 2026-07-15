using FunctionFoundry.Networking;

namespace FunctionFoundry.Networking.Tests;

public sealed class MirrorSelectorTests
{
    [Fact]
    public void Select_prefers_lower_latency_with_deterministic_ties()
    {
        var selector = new MirrorSelector();
        selector.Record(new MirrorObservation("a", 200, 1_000_000, true, true));
        selector.Record(new MirrorObservation("b", 50, 1_000_000, true, true));
        string selected = selector.Select(["a", "b"]);
        Assert.Equal("b", selected);
    }

    [Fact]
    public void Hysteresis_prevents_rapid_switching()
    {
        var selector = new MirrorSelector(new MirrorSelectorOptions { SwitchHysteresis = 0.2 });
        selector.Record(new MirrorObservation("a", 40, 2_000_000, true, true));
        selector.Record(new MirrorObservation("b", 60, 1_000_000, true, true));
        Assert.Equal("a", selector.Select(["a", "b"]));

        selector.Record(new MirrorObservation("b", 10, 3_000_000, true, true));
        Assert.Equal("a", selector.Select(["a", "b"]));
    }

    [Fact]
    public void ExportEvidence_contains_component_scores()
    {
        var selector = new MirrorSelector();
        selector.Record(new MirrorObservation("mirror-1", 80, 500_000, true, true));
        MirrorSelectionEvidence evidence = selector.ExportEvidence("mirror-1");
        Assert.Equal("mirror-1", evidence.MirrorId);
        Assert.InRange(evidence.Score, 0, 1);
        Assert.True(evidence.LatencyScore > 0);
    }

    [Fact]
    public void Select_throws_when_no_candidates()
    {
        var selector = new MirrorSelector();
        Assert.Throws<ArgumentException>(() => selector.Select([]));
    }

    [Fact]
    public void Options_validate_rejects_weights_that_do_not_sum_to_one()
    {
        var options = new MirrorSelectorOptions { LatencyWeight = 0.5, ThroughputWeight = 0.5, AvailabilityWeight = 0.5, IntegrityWeight = 0.5 };
        Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
    }
}
