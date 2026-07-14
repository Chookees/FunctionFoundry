using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using FunctionFoundry.Text;

namespace FunctionFoundry.Text.Benchmarks;

internal static class Program
{
    private static void Main(string[] args) => _ = BenchmarkRunner.Run<TextBenchmarks>(args: args);
}

[MemoryDiagnoser]
public class TextBenchmarks
{
    private string _text = null!;
    private string _csv = null!;

    [GlobalSetup]
    public void Setup()
    {
        _text = string.Join(' ', Enumerable.Repeat("the quick brown fox jumps over the lazy dog", 50));
        _csv = string.Join('\n', Enumerable.Range(0, 200).Select(i => $"{i},value{i},note{i}"));
    }

    [Benchmark]
    public UnicodeSpoofAnalysisResult SpoofAnalyze() => new UnicodeSpoofDetector().Analyze(_text);

    [Benchmark]
    public IReadOnlyList<SecretCandidateFinding> SecretScan() => new SecretCandidateScanner().Scan(_text);

    [Benchmark]
    public DelimitedTextDialectInferenceResult DialectInfer() => new DelimitedTextDialectInferrer().Infer(_csv);
}
