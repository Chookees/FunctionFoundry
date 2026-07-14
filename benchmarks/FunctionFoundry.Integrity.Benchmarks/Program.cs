using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using FunctionFoundry.Integrity;

namespace FunctionFoundry.Integrity.Benchmarks;

internal static class Program
{
    private static void Main(string[] args) => _ = BenchmarkRunner.Run<IntegrityBenchmarks>(args: args);
}

[MemoryDiagnoser]
public class IntegrityBenchmarks
{
    private byte[] _json = null!;
    private byte[][] _leaves = null!;
    private MerkleTree _tree = null!;
    private List<HashChainRecord> _chainRecords = null!;

    [GlobalSetup]
    public void Setup()
    {
        _json = Encoding.UTF8.GetBytes("{\"items\":[{\"b\":2,\"a\":1},{\"y\":true,\"x\":null}],\"count\":42}");
        _leaves = Enumerable.Range(0, 256).Select(i => Encoding.UTF8.GetBytes($"leaf-{i}")).ToArray();
        _tree = MerkleTree.Build(_leaves);
        _chainRecords = [HashChain.CreateGenesis("{\"start\":true}"u8)];
        for (int i = 0; i < 32; i++)
        {
            HashChainRecord next = HashChain.Append(_chainRecords[^1], Encoding.UTF8.GetBytes($"step-{i}"));
            _chainRecords.Add(next);
        }
    }

    [Benchmark]
    public byte[] CanonicalizeJson() => CanonicalJson.Canonicalize(_json);

    [Benchmark]
    public byte[] BuildManifest()
    {
        var builder = StreamingHashManifest.CreateBuilder();
        builder.AddEntry("data.bin", new MemoryStream(_json), includeSize: true);
        return builder.Build();
    }

    [Benchmark]
    public MerkleProof MerkleCreateProof() => _tree.CreateProof(127);

    [Benchmark]
    public HashChainVerificationResult VerifyHashChain() => HashChain.Verify(_chainRecords);
}
