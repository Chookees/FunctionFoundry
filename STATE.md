# FunctionFoundry State

Resume this file instead of conversation context.

## Current Feature

Health-scan remediation on branch `chore/health-scan-remediation` (SDK pin, NuGet release path, CI gates, Networking adaptive/repair, docs honesty).

## Current PBI

All PBIs PBI-01..PBI-12 delivered on `main`. Follow-up hardening in progress on remediation branch.

## Completed Tasks

* PBI-01..PBI-12 (v1.0 feature set)
* Git tag `1.0.0` on origin (no `v` prefix)
* Local and CI package production for 10 libraries

## Remaining Tasks

* Publish clean NuGet `1.0.0` via `.github/workflows/publish-nuget.yml` (requires `NUGET_API_KEY` secret); unlist or leave `1.0.0-local` as accidental pre-release
* Optional: Native AOT/trimming smoke apps before claiming `IsAotCompatible`
* Optional: raise branch coverage toward ≥90% on critical algorithms (Distributed/Integrity still the weakest)

## Last successful commands

* Target: `dotnet build -c Release` / `dotnet test -c Release` on SDK **10.0.302**
* Unit tests: **242** passed
* Line coverage: **85.8%** (CI gate ≥85%). Branch coverage not re-gated yet.

## Open technical risks

* Full Unicode confusable table not shipped (compact v1 subset documented).
* Observability redaction uses bounded reflection.
* `IsTrimmable` / `IsAotCompatible` not claimed.
* First stable NuGet line was mistakenly published as `1.0.0-local`; clean `1.0.0` publish still pending credentials.
* Package validation baseline against published `1.0.0` should be enabled after the clean publish.

## Decisions made

* ADRs 0001-0006
* Modular independent DLLs; zero third-party runtime dependencies
* Local packs keep `-local` suffix; release packs clear `VersionSuffix` (`CI=true` or `FF_RELEASE_PACK=true`)

## Exact next action

Land remediation PR, configure `NUGET_API_KEY`, run Publish NuGet workflow for `1.0.0`, then set `PackageValidationBaselineVersion` to `1.0.0`.
