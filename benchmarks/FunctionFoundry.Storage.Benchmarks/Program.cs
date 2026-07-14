using System.Security.Cryptography;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using FunctionFoundry.Storage;

namespace FunctionFoundry.Storage.Benchmarks;

internal static class Program
{
    private static void Main(string[] args) => _ = BenchmarkRunner.Run<StorageBenchmarks>(args: args);
}

[MemoryDiagnoser]
public class StorageBenchmarks
{
    private string _workDirectory = null!;
    private string _targetDirectory = null!;
    private string _storeRoot = null!;
    private byte[] _payload = null!;
    private ContentAddressedStore _store = null!;
    private string _storedHash = null!;
    private MerkleFileTreeSnapshot _snapshot = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "ff-storage-bench-" + Guid.NewGuid().ToString("N"));
        _targetDirectory = Path.Combine(_workDirectory, "target");
        _storeRoot = Path.Combine(_workDirectory, "cas");
        Directory.CreateDirectory(_targetDirectory);
        _payload = RandomNumberGenerator.GetBytes(256 * 1024);
        _store = new ContentAddressedStore(_storeRoot);
        ContentAddressedObjectInfo info = await _store.PutAsync(_payload).ConfigureAwait(false);
        _storedHash = info.ContentHashHex;
        var writer = await TransactionalFileSetWriter.BeginAsync(_targetDirectory).ConfigureAwait(false);
        await writer.StageFileAsync("data.bin", _payload).ConfigureAwait(false);
        await writer.CommitAsync().ConfigureAwait(false);
        _snapshot = await new MerkleFileTree().SnapshotAsync(_targetDirectory).ConfigureAwait(false);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(_workDirectory))
            {
                Directory.Delete(_workDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Benchmark]
    public async Task TransactionalCommit4K()
    {
        var writer = await TransactionalFileSetWriter.BeginAsync(_targetDirectory).ConfigureAwait(false);
        await writer.StageFileAsync("bench.txt", _payload.AsSpan(0, 4096).ToArray()).ConfigureAwait(false);
        await writer.CommitAsync().ConfigureAwait(false);
    }

    [Benchmark]
    public Task ContentAddressedPut256K() => _store.PutAsync(_payload);

    [Benchmark]
    public async Task ContentAddressedRead256K()
    {
        await using Stream stream = await _store.OpenReadAsync(_storedHash).ConfigureAwait(false);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory).ConfigureAwait(false);
    }

    [Benchmark]
    public async Task ContentDefinedChunk256K()
    {
        var chunker = new ContentDefinedChunker();
        _ = await chunker.ChunkAsync(_payload).ConfigureAwait(false);
    }

    [Benchmark]
    public Task MerkleSnapshot() => new MerkleFileTree().SnapshotAsync(_targetDirectory);

    [Benchmark]
    public MerkleFileTreeDiff MerkleCompare()
    {
        var tree = new MerkleFileTree();
        return tree.Compare(_snapshot, _snapshot);
    }
}
