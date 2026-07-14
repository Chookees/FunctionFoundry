# FunctionFoundry.Networking

## Design notes

### HttpClient lifetime

Callers must provide `HttpClient` (or factory-managed instances). The library issues requests through the supplied client only.

### Range support

Capability is detected with a lightweight `Range: bytes=0-0` probe. When ranges are unsupported, the downloader falls back to a single-stream transfer while still verifying the final digest.

### Checkpoints

Download and transfer checkpoints are persisted through abstractions so hosts can use files, object storage, or databases. Incompatible checkpoints (changed ETag, size, or plan version) are rejected explicitly.

### Integrity

`StreamingIntegrityVerifier` maintains per-chunk digests and a rolling full-object hash without buffering entire files in memory.
