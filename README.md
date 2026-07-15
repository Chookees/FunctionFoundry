# FunctionFoundry

[![CI](https://github.com/Chookees/UP_town_Funcs/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/Chookees/UP_town_Funcs/actions/workflows/ci.yml)
[![.NET](https://img.shields.io/badge/.NET-10.0.301-512BD4?logo=dotnet&logoColor=white)](global.json)
[![Tests](https://img.shields.io/badge/tests-224%20passed-brightgreen)](docs/coverage-summary.md)
[![Coverage](https://img.shields.io/badge/coverage-84.8%25%20line-yellowgreen)](docs/coverage-summary.md)
[![Branch Coverage](https://img.shields.io/badge/branch%20coverage-72.3%25-yellowgreen)](docs/coverage-summary.md)
[![Skipped](https://img.shields.io/badge/skipped-0-brightgreen)](docs/coverage-summary.md)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Packages](https://img.shields.io/badge/packages-10%20independent-blue)](#packages)

FunctionFoundry is a modular collection of independent, production-grade .NET libraries that provide specialized capabilities not supplied by the BCL or common Microsoft packages.

Each library ships as its own DLL and NuGet package. There is no mandatory aggregate package and no shared runtime "commons" dependency between core packages.

## Packages

Each product has a dedicated README under `src/<Package>/README.md` describing exactly what it does.

| Package | Purpose | README |
|---|---|---|
| `FunctionFoundry.Security` | Envelope encryption, deterministic pseudonymization, threshold secret sharing, key-rotation planning | [README](src/FunctionFoundry.Security/README.md) |
| `FunctionFoundry.Storage` | Transactional file-set writes, content-addressed storage, content-defined chunking, Merkle file-tree diffs | [README](src/FunctionFoundry.Storage/README.md) |
| `FunctionFoundry.Integrity` | Canonical JSON, hash manifests, Merkle proofs, tamper-evident hash chains | [README](src/FunctionFoundry.Integrity/README.md) |
| `FunctionFoundry.Observability` | Sensitive-data redaction, event fingerprinting, adaptive sampling, burst coalescing | [README](src/FunctionFoundry.Observability/README.md) |
| `FunctionFoundry.Data` | External merge sort, structural tree diff, three-way merge, temporal interval join | [README](src/FunctionFoundry.Data/README.md) |
| `FunctionFoundry.Text` | Unicode spoof detection, secret scanning, near-duplicate indexing, delimited-text dialect inference | [README](src/FunctionFoundry.Text/README.md) |
| `FunctionFoundry.Resilience` | Adaptive concurrency, hedged execution, execution budgets, checkpointed batch execution | [README](src/FunctionFoundry.Resilience/README.md) |
| `FunctionFoundry.Networking` | Resumable parallel downloads, mirror selection, transfer planning, streaming integrity verification | [README](src/FunctionFoundry.Networking/README.md) |
| `FunctionFoundry.Distributed` | Weighted rendezvous hashing, version clocks, phi-accrual failure detection, quorum aggregation | [README](src/FunctionFoundry.Distributed/README.md) |
| `FunctionFoundry.Scheduling` | Interval-set algebra, recurring availability, business calendars, critical-path scheduling | [README](src/FunctionFoundry.Scheduling/README.md) |

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

Coverage (optional):

```bash
dotnet test -c Release --collect:"XPlat Code Coverage" --results-directory ./artifacts/test-results
```

Package artifacts are written to `artifacts/packages/`. Full coverage breakdown: [docs/coverage-summary.md](docs/coverage-summary.md).

## Design principles

* Specialized functions with nontrivial algorithms, protocols, or guarantees
* Zero third-party runtime dependencies by default
* Independent package consumption
* Complete XML documentation
* Deterministic builds and reproducible packages where the SDK supports it

## License

MIT — see [LICENSE](LICENSE).
