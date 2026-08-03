# Unit test coverage summary

Measured with Coverlet Cobertura on a Release `net10.0` build (SDK **10.0.302**), aggregated by unique package name (same methodology as CI).

**Unit tests:** 242 passed, 0 failed, 0 skipped.

## Overall

| Metric | Value |
|---|---:|
| Line coverage (`src/` libraries) | **85.8%** (8858 / 10326) |
| CI gate | ≥ **85%** line (enforced) |

## By package

| Package | Line coverage |
|---|---:|
| `FunctionFoundry.Data` | 81.6% (992/1216) |
| `FunctionFoundry.Distributed` | 87.9% (782/890) |
| `FunctionFoundry.Integrity` | 85.9% (952/1108) |
| `FunctionFoundry.Networking` | 90.3% (672/744) |
| `FunctionFoundry.Observability` | 85.6% (1048/1224) |
| `FunctionFoundry.Resilience` | 81.2% (856/1054) |
| `FunctionFoundry.Scheduling` | 84.1% (784/932) |
| `FunctionFoundry.Security` | 90.4% (586/648) |
| `FunctionFoundry.Storage` | 85.6% (1364/1594) |
| `FunctionFoundry.Text` | 89.7% (822/916) |
| **Overall (src libraries)** | **85.8% (8858/10326)** |

## Pipeline note

CI fails when aggregated line coverage is below 85%. Branch-coverage ≥90% for critical algorithms remains an aspirational target (not yet a hard gate).
