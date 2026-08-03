using FunctionFoundry.Observability;

namespace FunctionFoundry.Observability.Tests;

public sealed class CardinalityLimiterTests
{
    [Fact]
    public void Allows_until_budget_then_overflows()
    {
        var limiter = new CardinalityLimiter(new CardinalityLimiterOptions { MaxKeysPerDimension = 2 });
        Assert.Equal(CardinalityDecisionKind.Allowed, limiter.Observe("user", "a").Kind);
        Assert.Equal(CardinalityDecisionKind.AlreadySeen, limiter.Observe("user", "a").Kind);
        Assert.Equal(CardinalityDecisionKind.Allowed, limiter.Observe("user", "b").Kind);
        CardinalityDecision overflow = limiter.Observe("user", "c");
        Assert.Equal(CardinalityDecisionKind.Overflow, overflow.Kind);
        Assert.Equal("__overflow__", overflow.EffectiveKey);
        Assert.Equal(2, limiter.GetTrackedCount("user"));
    }

    [Fact]
    public void Dimensions_are_isolated()
    {
        var limiter = new CardinalityLimiter(new CardinalityLimiterOptions { MaxKeysPerDimension = 1 });
        Assert.Equal(CardinalityDecisionKind.Allowed, limiter.Observe("a", "x").Kind);
        Assert.Equal(CardinalityDecisionKind.Allowed, limiter.Observe("b", "x").Kind);
    }

    [Fact]
    public void Rejects_blank_inputs()
    {
        var limiter = new CardinalityLimiter();
        Assert.Throws<ArgumentException>(() => limiter.Observe(" ", "k"));
        Assert.Throws<ArgumentException>(() => limiter.Observe("d", " "));
    }
}
