using System.Security.Cryptography;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using FunctionFoundry.Security;

namespace FunctionFoundry.Security.Benchmarks;

internal static class Program
{
    private static void Main(string[] args) => _ = BenchmarkRunner.Run<SecurityBenchmarks>(args: args);
}

[MemoryDiagnoser]
public class SecurityBenchmarks
{
    private EnvelopeEncryptor _encryptor = null!;
    private Pseudonymizer _pseudonymizer = null!;
    private byte[] _plaintext = null!;
    private byte[] _envelope = null!;
    private byte[] _secret = null!;

    [GlobalSetup]
    public void Setup()
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);
        _encryptor = new EnvelopeEncryptor(new DictionaryDataKeyResolver(new Dictionary<string, byte[]> { ["k"] = key }));
        _pseudonymizer = new Pseudonymizer(key, new PseudonymizerOptions("bench", "t", 1));
        _plaintext = RandomNumberGenerator.GetBytes(4096);
        _envelope = _encryptor.Encrypt("k", _plaintext, ReadOnlySpan<byte>.Empty);
        _secret = RandomNumberGenerator.GetBytes(32);
    }

    [Benchmark]
    public byte[] Encrypt4K() => _encryptor.Encrypt("k", _plaintext, ReadOnlySpan<byte>.Empty);

    [Benchmark]
    public byte[] Decrypt4K() => _encryptor.Decrypt(_envelope, ReadOnlySpan<byte>.Empty);

    [Benchmark]
    public string Pseudonymize() => _pseudonymizer.Pseudonymize(_plaintext.AsSpan(0, 32));

    [Benchmark]
    public byte[] ShamirSplitCombine()
    {
        IReadOnlyList<SecretShare> shares = SecretSharer.Split(_secret, 3, 5);
        return SecretSharer.Combine(shares.Take(3).ToArray());
    }
}
