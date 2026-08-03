# Changelog

All notable changes to FunctionFoundry packages are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.1.0] - 2026-08-03

### Added

* `FunctionFoundry.Security.HkdfDataKeyDeriver` — HKDF-SHA256 data-key derivation from a master key.
* `FunctionFoundry.Storage.ContentAddressedStore.ExecuteGarbageCollectionAsync` — apply orphan GC plans.
* `FunctionFoundry.Integrity.MerkleTree` multiproof create/verify APIs.
* `FunctionFoundry.Distributed.HybridLogicalClock` — hybrid logical timestamps for causal ordering.
* `FunctionFoundry.Text.HomoglyphNormalizer` — confusable folding to NFC skeletons.
* `FunctionFoundry.Observability.CardinalityLimiter` — bounded high-cardinality key admission.
* `FunctionFoundry.Resilience.RetryBudget` — token-budget gate for retry amplification control.
* `FunctionFoundry.Scheduling.BusinessCalendar.MeasureWorkingDuration` — measure working time between instants.
* `FunctionFoundry.Data.KWaySortedMerge<T>` — heap-based merge of already-sorted sequences.
* `FunctionFoundry.Networking.ContentRangeHeader` — parse/format HTTP Content-Range byte headers.

### Changed

* Package version prefix bumped to **1.1.0**.
* Pin .NET SDK **10.0.302** (maintenance upgrade from 10.0.301; roll-forward still disabled).
* Local packs keep the `-local` version suffix; CI and `FF_RELEASE_PACK=true` produce clean SemVer packages.
* CI runs on Ubuntu and Windows, builds samples, restores with lock files, and fails below 85% line coverage.
* `FunctionFoundry.Networking` downloader adapts chunk size and performs corrupt-chunk / digest repair.
* Security reporting documents GitHub Security Advisories as the private channel.

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
* An accidental NuGet publish of `1.0.0-local` should be replaced by a clean `1.0.0` / `1.1.0` using the Publish NuGet workflow.
