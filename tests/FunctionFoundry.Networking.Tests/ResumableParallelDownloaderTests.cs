using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using FunctionFoundry.Networking;

namespace FunctionFoundry.Networking.Tests;

internal sealed class FakeDownloadHandler : HttpMessageHandler
{
    private readonly byte[] _payload;

    public FakeDownloadHandler(string content) => _payload = Encoding.UTF8.GetBytes(content);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Headers.Range is RangeHeaderValue range && range.Ranges.Count > 0)
        {
            RangeItemHeaderValue item = range.Ranges.First();
            long from = item.From ?? 0;
            long to = item.To ?? (_payload.Length - 1);
            int length = (int)(to - from + 1);
            byte[] slice = _payload.AsSpan((int)from, length).ToArray();
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(slice),
            };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, _payload.Length);
            response.Headers.ETag = new EntityTagHeaderValue("\"v1\"");
            return Task.FromResult(response);
        }

        var full = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(_payload),
        };
        full.Content.Headers.ContentLength = _payload.Length;
        full.Headers.ETag = new EntityTagHeaderValue("\"v1\"");
        return Task.FromResult(full);
    }
}

internal static class Hashing
{
    public static string Sha256Hex(byte[] data)
    {
        byte[] hash = SHA256.HashData(data);
        var builder = new StringBuilder(hash.Length * 2);
        foreach (byte b in hash)
        {
            builder.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}

public sealed class ResumableParallelDownloaderTests
{
    [Fact]
    public async Task Range_download_verifies_hash_and_resumes()
    {
        const string content = "abcdefghijklmnopqrstuvwxyz0123456789";
        byte[] payload = Encoding.UTF8.GetBytes(content);
        string hash = Hashing.Sha256Hex(payload);
        using var handler = new FakeDownloadHandler(content);
        using var client = new HttpClient(handler);
        var store = new InMemoryDownloadCheckpointStore();
        var downloader = new ResumableParallelDownloader(
            client,
            store,
            new ResumableParallelDownloaderOptions
            {
                InitialChunkSizeBytes = 10,
                MinChunkSizeBytes = 10,
                MaxChunkSizeBytes = 20,
                MaxParallelism = 2,
            });

        var url = new Uri("https://example.test/file");
        await using MemoryStream destination = new();
        DownloadResult first = await downloader.DownloadAsync(url, destination, hash);
        Assert.True(first.Verified);
        Assert.True(first.UsedRangeRequests);
        Assert.Equal(payload.Length, first.BytesWritten);

        await store.SaveAsync(new DownloadCheckpoint(
            url,
            payload.Length,
            "\"v1\"",
            null,
            10,
            [0, 1],
            true));

        DownloadResult resumed = await downloader.DownloadAsync(url, destination, hash);
        Assert.True(resumed.ResumedFromCheckpoint);
        Assert.True(resumed.Verified);
    }

    [Fact]
    public async Task Falls_back_to_full_download_when_ranges_unsupported()
    {
        const string content = "full-download";
        byte[] payload = Encoding.UTF8.GetBytes(content);
        string hash = Hashing.Sha256Hex(payload);
        using var handler = new FullOnlyHandler(payload);
        using var client = new HttpClient(handler);
        var downloader = new ResumableParallelDownloader(client, new InMemoryDownloadCheckpointStore());
        var url = new Uri("https://example.test/full");
        await using MemoryStream destination = new();
        DownloadResult result = await downloader.DownloadAsync(url, destination, hash);
        Assert.True(result.Verified);
        Assert.False(result.UsedRangeRequests);
    }

    [Fact]
    public async Task Adaptive_chunk_size_stays_within_bounds_after_download()
    {
        const string content = "abcdefghijklmnopqrstuvwxyz0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        byte[] payload = Encoding.UTF8.GetBytes(content);
        string hash = Hashing.Sha256Hex(payload);
        using var handler = new FakeDownloadHandler(content);
        using var client = new HttpClient(handler);
        var options = new ResumableParallelDownloaderOptions
        {
            InitialChunkSizeBytes = 16,
            MinChunkSizeBytes = 8,
            MaxChunkSizeBytes = 32,
            MaxParallelism = 2,
            EnableAdaptiveChunkSizing = true,
        };
        var downloader = new ResumableParallelDownloader(client, new InMemoryDownloadCheckpointStore(), options);
        await using MemoryStream destination = new();
        DownloadResult result = await downloader.DownloadAsync(new Uri("https://example.test/adaptive"), destination, hash);
        Assert.True(result.Verified);
        Assert.InRange(downloader.AdaptiveChunkSizeBytes, options.MinChunkSizeBytes, options.MaxChunkSizeBytes);
    }

    [Fact]
    public async Task Corrupt_chunk_length_is_repaired()
    {
        const string content = "0123456789abcdef";
        byte[] payload = Encoding.UTF8.GetBytes(content);
        string hash = Hashing.Sha256Hex(payload);
        using var handler = new FlakyLengthHandler(payload, corruptOnceChunkIndex: 1);
        using var client = new HttpClient(handler);
        var downloader = new ResumableParallelDownloader(
            client,
            new InMemoryDownloadCheckpointStore(),
            new ResumableParallelDownloaderOptions
            {
                InitialChunkSizeBytes = 4,
                MinChunkSizeBytes = 4,
                MaxChunkSizeBytes = 8,
                MaxParallelism = 1,
                MaxRepairPasses = 1,
            });

        await using MemoryStream destination = new();
        DownloadResult result = await downloader.DownloadAsync(new Uri("https://example.test/repair"), destination, hash);
        Assert.True(result.Verified);
        Assert.True(handler.CorruptResponses > 0);
    }

    private sealed class FullOnlyHandler : HttpMessageHandler
    {
        private readonly byte[] _payload;

        public FullOnlyHandler(byte[] payload) => _payload = payload;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_payload),
            };
            response.Content.Headers.ContentLength = _payload.Length;
            return Task.FromResult(response);
        }
    }

    private sealed class FlakyLengthHandler : HttpMessageHandler
    {
        private readonly byte[] _payload;
        private readonly int _corruptOnceChunkIndex;
        private int _rangeCalls;

        public FlakyLengthHandler(byte[] payload, int corruptOnceChunkIndex)
        {
            _payload = payload;
            _corruptOnceChunkIndex = corruptOnceChunkIndex;
        }

        public int CorruptResponses { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Headers.Range is RangeHeaderValue range && range.Ranges.Count > 0)
            {
                RangeItemHeaderValue item = range.Ranges.First();
                long from = item.From ?? 0;
                long to = item.To ?? (_payload.Length - 1);
                int length = (int)(to - from + 1);
                int call = Interlocked.Increment(ref _rangeCalls) - 1;
                byte[] slice = _payload.AsSpan((int)from, length).ToArray();
                if (call == _corruptOnceChunkIndex)
                {
                    CorruptResponses++;
                    slice = slice.AsSpan(0, Math.Max(1, slice.Length - 1)).ToArray();
                }

                var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = new ByteArrayContent(slice),
                };
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, _payload.Length);
                response.Headers.ETag = new EntityTagHeaderValue("\"v1\"");
                return Task.FromResult(response);
            }

            var full = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_payload),
            };
            full.Content.Headers.ContentLength = _payload.Length;
            return Task.FromResult(full);
        }
    }
}
