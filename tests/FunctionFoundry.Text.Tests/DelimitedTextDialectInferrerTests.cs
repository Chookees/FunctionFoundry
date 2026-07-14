namespace FunctionFoundry.Text.Tests;

public sealed class DelimitedTextDialectInferrerTests
{
    [Fact]
    public void Ranks_csv_candidate_with_evidence()
    {
        const string sample = """
            name,age,score
            alice,30,9.5
            bob,25,8.1
            """;
        var inferrer = new DelimitedTextDialectInferrer();
        DelimitedTextDialectInferenceResult result = inferrer.Infer(sample);

        Assert.NotEmpty(result.Candidates);
        DelimitedTextDialectCandidate top = result.Candidates[0];
        Assert.Equal(',', top.Delimiter);
        Assert.True(top.Confidence < 1.0);
        Assert.NotEmpty(top.Evidence);
    }

    [Fact]
    public void Truncation_is_reported()
    {
        var inferrer = new DelimitedTextDialectInferrer(new DelimitedTextDialectInferrerOptions(MaximumSampleChars: 8));
        DelimitedTextDialectInferenceResult result = inferrer.Infer("a,b,c,d,e,f,g,h,i,j");
        Assert.True(result.Truncated);
        Assert.Equal(8, result.SampleChars);
    }

    [Fact]
    public void Empty_sample_is_rejected()
    {
        var inferrer = new DelimitedTextDialectInferrer();
        Assert.Throws<ArgumentException>(() => inferrer.Infer(string.Empty));
    }
}
