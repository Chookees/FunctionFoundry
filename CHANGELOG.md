# Changelog

All notable changes to FunctionFoundry packages are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
* Repository engineering foundation (SDK 10.0.301 pin, analyzers, CI, ADRs, documentation).

### Notes

* Feature 1 packages were hardened as the conceptual v0.5.0 baseline; Feature 2 completion is released as v1.0.0 local package artifacts.
* No mandatory aggregate package. Core packages have zero third-party runtime dependencies.
