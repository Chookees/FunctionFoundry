namespace FunctionFoundry.Observability.Tests;

public sealed class BurstCoalescerTests
{
    [Fact]
    public void Record_preserves_first_and_last_payload()
    {
        var clock = new MutableClock(DateTimeOffset.Parse("2026-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        var coalescer = new BurstCoalescer(new BurstCoalescerOptions { Window = TimeSpan.FromSeconds(5) }, () => clock.Now);

        coalescer.Record("fp", "first");
        coalescer.Record("fp", "middle");
        coalescer.Record("fp", "last");

        IReadOnlyList<BurstSummary> summaries = coalescer.FlushAll();
        BurstSummary summary = Assert.Single(summaries);
        Assert.Equal("first", summary.First);
        Assert.Equal("last", summary.Last);
        Assert.Equal(3, summary.TotalCount);
    }

    [Fact]
    public void Record_reports_dropped_counts_for_large_bursts()
    {
        var coalescer = new BurstCoalescer(new BurstCoalescerOptions { MaxSamplesPerBurst = 2 });
        for (int i = 0; i < 20; i++)
        {
            coalescer.Record("fp", i);
        }

        BurstSummary summary = Assert.Single(coalescer.FlushAll());
        Assert.Equal(20, summary.TotalCount);
        Assert.True(summary.DroppedCount > 0);
        Assert.True(summary.Samples.Count <= 2);
    }

    [Fact]
    public void Record_rotates_window_and_queues_pending_summary()
    {
        var clock = new MutableClock(DateTimeOffset.Parse("2026-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        var coalescer = new BurstCoalescer(new BurstCoalescerOptions { Window = TimeSpan.FromSeconds(10) }, () => clock.Now);
        coalescer.Record("fp", 1);
        coalescer.Record("fp", 2);
        clock.Advance(TimeSpan.FromSeconds(11));
        coalescer.Record("fp", 3);

        IReadOnlyList<BurstSummary> pending = coalescer.DrainPending();
        BurstSummary closed = Assert.Single(pending);
        Assert.Equal(2, closed.TotalCount);
        Assert.Equal(1, closed.First);
        Assert.Equal(2, closed.Last);
    }

    [Fact]
    public void Record_enforces_fingerprint_cardinality_bound()
    {
        var coalescer = new BurstCoalescer(new BurstCoalescerOptions { MaxFingerprints = 8 });
        for (int i = 0; i < 32; i++)
        {
            coalescer.Record($"fp-{i}", i);
        }

        Assert.Equal(8, coalescer.ActiveFingerprintCount);
    }

    [Fact]
    public void Record_supports_concurrent_producers()
    {
        var coalescer = new BurstCoalescer();
        Parallel.For(0, 100, i => coalescer.Record("fp", i));
        BurstSummary summary = Assert.Single(coalescer.FlushAll());
        Assert.Equal(100, summary.TotalCount);
    }

    [Fact]
    public void Options_validate_rejects_non_positive_window()
    {
        var options = new BurstCoalescerOptions { Window = TimeSpan.Zero };
        Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
    }

    [Fact]
    public void Record_throws_for_blank_fingerprint()
    {
        var coalescer = new BurstCoalescer();
        Assert.Throws<ArgumentException>(() => coalescer.Record("  ", "payload"));
    }

    private sealed class MutableClock(DateTimeOffset start)
    {
        private DateTimeOffset _now = start;

        internal DateTimeOffset Now => _now;

        internal void Advance(TimeSpan delta) => _now += delta;
    }
}
