# FunctionFoundry.Networking

Resumable, integrity-aware HTTP transfer orchestration using caller-provided `HttpClient` instances.

## Capabilities

* **Resumable parallel downloader** — HTTP ranges with ETag/Last-Modified validation, adaptive chunks, checkpoint resume, and final digest verification
* **Mirror selector** — multi-signal scoring with failure decay, hysteresis, and exportable evidence
* **Transfer plan** — chunk assignments independent of execution with overlap detection and corrupt-chunk repair
* **Streaming integrity verifier** — bounded-memory incremental per-chunk and full-object digest verification

## Runtime dependencies

None beyond the .NET shared framework.

## Usage note

Always inject a shared `HttpClient` (or `IHttpClientFactory`-managed instance). This library never creates per-request `HttpClient` instances.
