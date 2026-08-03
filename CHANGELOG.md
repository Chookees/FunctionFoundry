# Changelog

All notable changes to FunctionFoundry packages are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

* Pin .NET SDK **10.0.302** (maintenance upgrade from 10.0.301; roll-forward still disabled).
* Local packs keep the `-local` version suffix; CI and `FF_RELEASE_PACK=true` produce clean SemVer packages suitable for NuGet.org.
* CI now runs on Ubuntu and Windows, builds all samples, restores with lock files, and fails when aggregated line coverage is below 85%.
* `FunctionFoundry.Networking` downloader now adapts chunk size within min/max from observed throughput and performs corrupt-chunk / digest repair passes.
* Security reporting documents GitHub Security Advisories as the private channel.

### Added

* `.github/workflows/publish-nuget.yml` for clean version publishes (requires `NUGET_API_KEY`).
* Dependabot for GitHub Actions and NuGet.
* `CODE_OF_CONDUCT.md`.
* Package lock files (`packages.lock.json`) for reproducible restores.

### Fixed

* Documentation drift (test counts, tag naming, release/NuGet status) in `STATE.md` and `docs/FINAL_REPORT.md`.
* Changelog now records the Apache 2.0 relicense under 1.0.0 (was incorrectly left only under Unreleased).

## [1.0.0] - 2026-07-14

### Added

* `FunctionFoundry.Security` — envelope encryption, deterministic pseudonymization, Shamir secret sharing, key rotation planning.
* `FunctionFoundry.Storage` — transactional file-set writer, content-addressed store, content-defined chunking, Merkle file-tree diff.
* `FunctionFoundry.Integrity` — canonical JSON, streaming hash manifests, Merkle proofs, hash chains.
* `FunctionFoundry.Observability` — sensitive-data redaction, event fingerprinting, adaptive sampling, burst coalescing.
* `FunctionFoundry.Data` — external merge sort, structural tree diff, three-way merge, temporal interval join.
* `FunctionFoundry.Text` — Unicode spoof detection, secret candidate scanner, near-duplicate index, delimited dialect inference.
* `FunctionFoundry.Resilience` — adaptive concurrency, hedged execution, execution budgets, checkpointed batch executor.
* `FunctionFoundry.Networking` — resumable parallel downloader, mirror selector, transfer plan, streaming integrity verifier.
* `FunctionFoundry.Distributed` — weighted rendezvous hashing, version clocks, phi-accrual detector, quorum aggregator.
* `FunctionFoundry.Scheduling` — interval-set algebra, recurring availability, business calendars, critical-path scheduler.
* Repository engineering foundation (SDK pin, analyzers, CI, ADRs, documentation).

### Changed

* Licensed under the **Apache License 2.0** with an attribution `NOTICE` file. Free use remains allowed; FunctionFoundry must be credited when used or redistributed.

### Notes

* Feature 1 packages were hardened as the conceptual v0.5.0 baseline; Feature 2 completion is released as v1.0.0.
* No mandatory aggregate package. Core packages have zero third-party runtime dependencies.
* An accidental NuGet publish of `1.0.0-local` should be replaced by a clean `1.0.0` using the Publish NuGet workflow.
