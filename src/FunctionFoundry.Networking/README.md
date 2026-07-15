# FunctionFoundry.Networking

Robust large-data transfer orchestration over HTTP using a **caller-provided** `HttpClient`.

## What this product does

It plans and executes **resumable, integrity-aware downloads** with mirror selection and streaming verification.

### 1. Resumable parallel downloader (`ResumableParallelDownloader`)

Downloads large objects using HTTP range requests when available:

* ETag / Last-Modified validation
* persistent checkpoint format
* configurable parallelism
* partial-file integrity and final cryptographic verification
* safe restart after interruption
* capability detection and correct fallback when ranges are unsupported

Never creates a new `HttpClient` per request.

### 2. Mirror selector (`MirrorSelector`)

Scores candidate mirrors using latency, throughput, availability, and integrity failures:

* failure decay over time
* hysteresis to avoid rapid oscillation
* deterministic tie-breaking
* exportable selection evidence

### 3. Transfer plan (`TransferPlan`)

Generates chunk assignments independently from execution:

* resumable state
* incompatible checkpoint detection
* no overlapping writes
* full-coverage verification
* repair assignments after corrupt chunks

### 4. Streaming integrity verifier (`StreamingIntegrityVerifier`)

Verifies data while it streams:

* per-chunk and complete-object checks
* detailed mismatch location
* bounded memory (no full-file buffering)

## When to use it

* IDE/plugin/runtime update downloaders
* Multi-CDN artifact fetchers
* Resumable media or dataset transfers with integrity requirements

## Non-goals

* Owning `HttpClient` lifetime / DNS / proxy policy for the host
* A general REST client library

## Install

```bash
dotnet add package FunctionFoundry.Networking
```

## Runtime dependencies

None beyond the .NET shared framework (`HttpClient` supplied by the caller).
