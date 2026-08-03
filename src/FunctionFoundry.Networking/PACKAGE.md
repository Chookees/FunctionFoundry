# FunctionFoundry.Networking

Resumable, integrity-aware HTTP transfer orchestration using caller-provided `HttpClient` instances.

## Capabilities

* **Resumable parallel downloader** — HTTP ranges with ETag/Last-Modified validation, adaptive chunk sizing within configured min/max bounds, checkpoint resume, corrupt-chunk repair passes, and final digest verification
* **Mirror selector** — multi-signal scoring with failure decay, hysteresis, and exportable evidence
* **Transfer plan** — chunk assignments independent of execution with overlap detection and corrupt-chunk repair assignment helpers
* **Streaming integrity verifier** — bounded-memory incremental per-chunk and full-object digest verification
* **Content-Range header** — parse and format HTTP byte Content-Range values for transfer planning

## Runtime dependencies

None beyond the .NET shared framework.

## Usage note

Always inject a shared `HttpClient` (or `IHttpClientFactory`-managed instance). This library never creates per-request `HttpClient` instances.

Adaptive sizing observes per-chunk throughput and smooths the next recommended chunk size within `MinChunkSizeBytes`/`MaxChunkSizeBytes`. Repair re-downloads length-mismatched chunks immediately and can re-fetch the full plan when the final object digest fails (`MaxRepairPasses`).
