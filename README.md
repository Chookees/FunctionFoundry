# FunctionFoundry

## Pipeline & quality

| Check | Result |
|---|---|
| CI workflow | [`.github/workflows/ci.yml`](.github/workflows/ci.yml) — restore, format, Release build, all unit tests, pack |
| Latest green CI (PR #2) | [success · run 29378869693](https://github.com/Chookees/UP_town_Funcs/actions/runs/29378869693) |
| Unit tests | **224 passed**, **0 failed**, **0 skipped** |
| Line coverage (production `src/`) | **84.8%** (4327 / 5103) |
| Branch coverage (production `src/`) | **72.3%** (1791 / 2478) |
| SDK | **10.0.301** (`global.json`, roll-forward disabled) |

Coverage was measured with Coverlet Cobertura on a Release build (`dotnet test --collect:"XPlat Code Coverage"`). Engineering tests are excluded from library totals. Detailed table: [docs/coverage-summary.md](docs/coverage-summary.md).

### Unit tests by package

| Test project | Passed | Skipped |
|---|---:|---:|
| FunctionFoundry.Security.Tests | 17 | 0 |
| FunctionFoundry.Storage.Tests | 35 | 0 |
| FunctionFoundry.Integrity.Tests | 39 | 0 |
| FunctionFoundry.Observability.Tests | 27 | 0 |
| FunctionFoundry.Data.Tests | 18 | 0 |
| FunctionFoundry.Text.Tests | 18 | 0 |
| FunctionFoundry.Resilience.Tests | 16 | 0 |
| FunctionFoundry.Networking.Tests | 14 | 0 |
| FunctionFoundry.Distributed.Tests | 18 | 0 |
| FunctionFoundry.Scheduling.Tests | 19 | 0 |
| FunctionFoundry.Engineering.Tests | 3 | 0 |
| **Total** | **224** | **0** |

### Line / branch coverage by package

| Package | Line coverage | Branch coverage |
|---|---:|---:|
| `FunctionFoundry.Data` | 81.6% (496/608) | 72.6% (231/318) |
| `FunctionFoundry.Distributed` | 77.8% (346/445) | 62.1% (154/248) |
| `FunctionFoundry.Integrity` | 84.6% (468/553) | 67.8% (213/314) |
| `FunctionFoundry.Networking` | 91.7% (287/313) | 76.0% (111/146) |
| `FunctionFoundry.Observability` | 85.5% (523/612) | 71.3% (231/324) |
| `FunctionFoundry.Resilience` | 81.2% (428/527) | 70.9% (139/196) |
| `FunctionFoundry.Scheduling` | 84.1% (392/466) | 75.6% (186/246) |
| `FunctionFoundry.Security` | 90.4% (293/324) | 72.8% (99/136) |
| `FunctionFoundry.Storage` | 85.7% (683/797) | 75.5% (240/318) |
| `FunctionFoundry.Text` | 89.7% (411/458) | 80.6% (187/232) |
| **Overall (src libraries)** | **84.8% (4327/5103)** | **72.3% (1791/2478)** |

CI runs every test project without stopping early and fails if any test is reported as skipped.

---

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

Package artifacts are written to `artifacts/packages/`.

## Design principles

* Specialized functions with nontrivial algorithms, protocols, or guarantees
* Zero third-party runtime dependencies by default
* Independent package consumption
* Complete XML documentation
* Deterministic builds and reproducible packages where the SDK supports it

## License

MIT — see [LICENSE](LICENSE).
