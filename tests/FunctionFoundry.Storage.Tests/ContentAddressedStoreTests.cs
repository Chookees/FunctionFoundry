using System.Security.Cryptography;
using System.Text;
using FunctionFoundry.Storage;

namespace FunctionFoundry.Storage.Tests;

public sealed class ContentAddressedStoreTests
{
    [Fact]
    public async Task Put_and_read_round_trip()
    {
        string root = CreateTempDirectory();
        var store = new ContentAddressedStore(root);
        byte[] payload = Encoding.UTF8.GetBytes("content-addressed");
        ContentAddressedObjectInfo info = await store.PutAsync(payload);
        Assert.False(info.WasDeduplicated);
        byte[] roundTrip = await store.ReadAllBytesAsync(info.ContentHashHex);
        Assert.Equal(payload, roundTrip);
    }

    [Fact]
    public async Task Put_deduplicates_identical_content()
    {
        string root = CreateTempDirectory();
        var store = new ContentAddressedStore(root);
        byte[] payload = RandomNumberGenerator.GetBytes(1024);
        ContentAddressedObjectInfo first = await store.PutAsync(payload);
        ContentAddressedObjectInfo second = await store.PutAsync(payload);
        Assert.Equal(first.ContentHashHex, second.ContentHashHex);
        Assert.True(second.WasDeduplicated);
        Assert.Single(Directory.EnumerateFiles(Path.Combine(root, "objects"), "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task OpenRead_throws_when_object_missing()
    {
        string root = CreateTempDirectory();
        var store = new ContentAddressedStore(root);
        await Assert.ThrowsAsync<FileNotFoundException>(() => store.OpenReadAsync(new string('a', 64)));
    }

    [Fact]
    public async Task ReadAllBytes_detects_tampering()
    {
        string root = CreateTempDirectory();
        var store = new ContentAddressedStore(root);
        ContentAddressedObjectInfo info = await store.PutAsync("tamper"u8.ToArray());
        string objectPath = Directory.EnumerateFiles(Path.Combine(root, "objects"), "*", SearchOption.AllDirectories).Single();
        await File.WriteAllTextAsync(objectPath, "broken");
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReadAllBytesAsync(info.ContentHashHex));
    }

    [Fact]
    public async Task PlanGarbageCollection_lists_orphans_deterministically()
    {
        string root = CreateTempDirectory();
        var store = new ContentAddressedStore(root);
        ContentAddressedObjectInfo kept = await store.PutAsync("keep"u8.ToArray());
        ContentAddressedObjectInfo orphan = await store.PutAsync("orphan"u8.ToArray());
        ContentAddressedGcPlan plan = await store.PlanGarbageCollectionAsync(new HashSet<string>(StringComparer.Ordinal) { kept.ContentHashHex });
        Assert.Single(plan.Candidates);
        Assert.Equal(orphan.ContentHashHex, plan.Candidates[0].ContentHashHex);
        Assert.Equal(orphan.SizeBytes, plan.TotalReclaimableBytes);
        string firstText = plan.ToDeterministicText();
        string secondText = (await store.PlanGarbageCollectionAsync(new HashSet<string>(StringComparer.Ordinal) { kept.ContentHashHex })).ToDeterministicText();
        Assert.Equal(firstText, secondText);
    }

    [Fact]
    public async Task Concurrent_puts_succeed_for_same_content()
    {
        string root = CreateTempDirectory();
        var store = new ContentAddressedStore(root);
        byte[] payload = RandomNumberGenerator.GetBytes(2048);
        Task<ContentAddressedObjectInfo>[] tasks = Enumerable.Range(0, 8)
            .Select(_ => store.PutAsync(payload.ToArray()))
            .ToArray();
        ContentAddressedObjectInfo[] results = await Task.WhenAll(tasks);
        Assert.All(results, result => Assert.Equal(results[0].ContentHashHex, result.ContentHashHex));
        Assert.Single(Directory.EnumerateFiles(Path.Combine(root, "objects"), "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Put_honors_cancellation()
    {
        string root = CreateTempDirectory();
        var store = new ContentAddressedStore(root);
        await using var stream = new SlowStream(Encoding.UTF8.GetBytes("cancel"));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.PutAsync(stream, cts.Token));
    }

    [Fact]
    public void Exists_returns_false_for_invalid_hash_length()
    {
        string root = CreateTempDirectory();
        var store = new ContentAddressedStore(root);
        Assert.Throws<ArgumentException>(() => store.Exists("abc"));
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "ff-cas-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class SlowStream(byte[] payload) : MemoryStream(payload)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            return await base.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
    }
}
