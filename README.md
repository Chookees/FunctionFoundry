# FunctionFoundry

FunctionFoundry is a modular collection of independent, production-grade .NET libraries that provide specialized capabilities not supplied by the BCL or common Microsoft packages.

Each library ships as its own DLL and NuGet package. There is no mandatory aggregate package and no shared runtime "commons" dependency between core packages.

## Packages

| Package | Purpose |
|---|---|
| `FunctionFoundry.Security` | Envelope encryption, deterministic pseudonymization, threshold secret sharing, key-rotation planning |
| `FunctionFoundry.Storage` | Transactional file-set writes, content-addressed storage, content-defined chunking, Merkle file-tree diffs |
| `FunctionFoundry.Integrity` | Canonical JSON, hash manifests, Merkle proofs, tamper-evident hash chains |
| `FunctionFoundry.Observability` | Sensitive-data redaction, event fingerprinting, adaptive sampling, burst coalescing |
| `FunctionFoundry.Data` | External merge sort, structural tree diff, three-way merge, temporal interval join |
| `FunctionFoundry.Text` | Unicode spoof detection, secret scanning, near-duplicate indexing, delimited-text dialect inference |
| `FunctionFoundry.Resilience` | Adaptive concurrency, hedged execution, execution budgets, checkpointed batch execution |
| `FunctionFoundry.Networking` | Resumable parallel downloads, mirror selection, transfer planning, streaming integrity verification |
| `FunctionFoundry.Distributed` | Weighted rendezvous hashing, version clocks, phi-accrual failure detection, quorum aggregation |
| `FunctionFoundry.Scheduling` | Interval-set algebra, recurring availability, business calendars, critical-path scheduling |

## Requirements

* .NET SDK **10.0.301** (pinned in `global.json`; roll-forward disabled)
* Target framework: `net10.0`
* C# 14.0

SDK versions receive explicit maintenance upgrades only. Do not silently change the pinned feature band.

## Build

```bash
dotnet restore
dotnet build -c Release
dotnet test -c Release
dotnet pack -c Release
```

Package artifacts are written to `artifacts/packages/`.

## Design principles

* Specialized functions with nontrivial algorithms, protocols, or guarantees
* Zero third-party runtime dependencies by default
* Independent package consumption
* Complete XML documentation
* Deterministic builds and reproducible packages where the SDK supports it

## License

MIT — see [LICENSE](LICENSE).
