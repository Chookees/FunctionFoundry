# FunctionFoundry State

Resume this file instead of conversation context.

## Current Feature

FF-F02 complete; FunctionFoundry v1.0.0 local artifacts generated.

## Current PBI

All PBIs PBI-01..PBI-12 delivered on branch `cursor/functionfoundry-v1-482e` / `main`.

## Completed Tasks

* PBI-01 foundation
* PBI-02 Security
* PBI-03 Storage
* PBI-04 Integrity
* PBI-05 Observability
* PBI-06 hardening report / size report / docs
* PBI-07 Data
* PBI-08 Text
* PBI-09 Resilience
* PBI-10 Networking
* PBI-11 Distributed
* PBI-12 Scheduling + v1.0 packaging

## Remaining Tasks

* Optional: NuGet.org publish (requires credentials/permission)
* Optional: git tags `v0.5.0` / `v1.0.0` if tagging succeeds on remote
* Optional: Native AOT/trimming smoke apps before claiming IsAotCompatible

## Last successful commands

* `dotnet build -c Release` — 0 warnings/errors (all 10 packages + tests/samples/benchmarks)
* `dotnet test -c Release` — **182 passed**
* `dotnet pack -c Release -o artifacts/packages /p:Version=1.0.0` — 10 nupkg + snupkg

## Last successful commit

Recorded after hardening commit / main fast-forward (see git log).

## Main-branch PBI commits (expected)

* PBI-01: `86bc024` build(repo)...
* PBI-02: `b89ed86` feat(security)...
* PBI-04: `ba3a4fb` feat(integrity)...
* PBI-05: `1a5a724` feat(observability)...
* PBI-03: `f50a08e` feat(storage)...
* PBI-07..12: see `git log --oneline` on delivery branch (Data/Text/Resilience/Networking/Distributed/Scheduling)

## Open technical risks

* Full Unicode confusable table not shipped (compact v1 subset documented).
* Networking adaptive chunk resize and auto-repair of corrupt chunks are partial relative to maximal interpretation of the brief.
* Observability redaction uses bounded reflection.
* IsTrimmable/IsAotCompatible not claimed.
* Coverage thresholds not yet enforced via CI report thresholds tooling (tests pass; coverage gate not numerically asserted in CI YAML).
* Package validation against a prior published baseline not applicable for first release.

## Decisions made

* ADRs 0001-0005
* Modular independent DLLs; no FunctionProvider code copied
* Zero third-party runtime dependencies across all core packages

## Known deviations

* Cloud agent branches use `cursor/...-482e` naming.
* PBI commit order on main is not strictly numeric (Integrity/Observability landed before Storage).
* Separate Feature 1 `v0.5.0` git tag may be skipped if tagging Focuses on `v1.0.0` only.

## Exact next action

Commit hardening docs, push delivery branch + main, open PR, attempt `v1.0.0` tag.
