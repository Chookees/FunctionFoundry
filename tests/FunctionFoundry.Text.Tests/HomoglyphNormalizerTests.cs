using FunctionFoundry.Text;

namespace FunctionFoundry.Text.Tests;

public sealed class HomoglyphNormalizerTests
{
    [Fact]
    public void Folds_cyrillic_lookalikes_to_ascii_skeleton()
    {
        var normalizer = new HomoglyphNormalizer();
        HomoglyphNormalizationResult result = normalizer.NormalizeToSkeleton("раypаl");
        Assert.Equal("paypal", result.Skeleton);
        Assert.True(result.Substitutions >= 2);
    }

    [Fact]
    public void Rejects_oversized_input()
    {
        var normalizer = new HomoglyphNormalizer(new HomoglyphNormalizerOptions(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => normalizer.NormalizeToSkeleton("abcde"));
    }

    [Fact]
    public void Leaves_plain_ascii_unchanged()
    {
        var normalizer = new HomoglyphNormalizer();
        HomoglyphNormalizationResult result = normalizer.NormalizeToSkeleton("hello");
        Assert.Equal("hello", result.Skeleton);
        Assert.Equal(0, result.Substitutions);
    }
}
