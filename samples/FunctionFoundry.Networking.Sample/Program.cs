using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using FunctionFoundry.Networking;

byte[] payload = Encoding.UTF8.GetBytes("function-foundry-networking-sample-payload");
string hash = Convert.ToHexString(SHA256.HashData(payload)).ToUpperInvariant();

using var handler = new SampleHandler(payload);
using HttpClient client = new(handler, disposeHandler: true);
var downloader = new ResumableParallelDownloader(client, new InMemoryDownloadCheckpointStore());
await using MemoryStream destination = new();
DownloadResult result = await downloader.DownloadAsync(new Uri("https://example.test/sample"), destination, hash);
Console.WriteLine($"Downloaded {result.BytesWritten} bytes, verified={result.Verified}, ranges={result.UsedRangeRequests}");

var selector = new MirrorSelector();
selector.Record(new MirrorObservation("mirror-a", 40, 2_000_000, true, true));
selector.Record(new MirrorObservation("mirror-b", 90, 1_500_000, true, true));
Console.WriteLine($"Selected mirror: {selector.Select(["mirror-a", "mirror-b"])}");

internal sealed class SampleHandler : HttpMessageHandler
{
    private readonly byte[] _payload;

    public SampleHandler(byte[] payload) => _payload = payload;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Headers.Range is not null)
        {
            RangeItemHeaderValue range = request.Headers.Range.Ranges.First();
            int from = (int)(range.From ?? 0);
            int to = (int)(range.To ?? (_payload.Length - 1));
            byte[] slice = _payload[from..(to + 1)];
            HttpResponseMessage response = new(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(slice),
            };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, _payload.Length);
            return Task.FromResult(response);
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_payload) });
    }
}
