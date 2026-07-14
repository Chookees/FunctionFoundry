namespace FunctionFoundry.Text.Tests;

public sealed class NearDuplicateTextIndexTests
{
    [Fact]
    public void Finds_near_duplicate_documents()
    {
        var index = new NearDuplicateTextIndex(new NearDuplicateTextIndexOptions(SimilarityThreshold: 0.4, Seed: 42));
        const string baseText = "the quick brown fox jumps over the lazy dog repeatedly";
        index.Upsert("a", baseText);
        index.Upsert("b", baseText + " today");

        IReadOnlyList<NearDuplicateCandidate> matches = index.Query("a");
        Assert.NotEmpty(matches);
        Assert.True(matches[0].EstimatedSimilarity >= 0.4);
    }

    [Fact]
    public void Remove_updates_index()
    {
        var index = new NearDuplicateTextIndex();
        index.Upsert("a", "one two three four five six seven");
        index.Upsert("b", "one two three four five six seven eight");
        Assert.True(index.Remove("b"));
        Assert.Equal(1, index.DocumentCount);
    }

    [Fact]
    public void Deterministic_seed_produces_repeatable_candidates()
    {
        var index1 = new NearDuplicateTextIndex(new NearDuplicateTextIndexOptions(Seed: 99));
        var index2 = new NearDuplicateTextIndex(new NearDuplicateTextIndexOptions(Seed: 99));
        const string text = "alpha beta gamma delta epsilon zeta eta theta";
        index1.Upsert("x", text);
        index1.Upsert("y", text + " extra");
        index2.Upsert("x", text);
        index2.Upsert("y", text + " extra");

        double sim1 = index1.Query("x")[0].EstimatedSimilarity;
        double sim2 = index2.Query("x")[0].EstimatedSimilarity;
        Assert.Equal(sim1, sim2);
    }
}
