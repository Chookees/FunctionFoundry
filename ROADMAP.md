# FunctionFoundry Roadmap

This roadmap defines Features FF-F01 and FF-F02, twelve PBIs, and Task-sized work units. Effort labels describe scope complexity, not calendar waiting time.

**Planned final commit message (v1.0):** `feat(scheduling): complete FunctionFoundry v1.0 [PBI-12]`

**Definition of Done (every PBI):** See section 17 of the master product definition — release build, tests, coverage, XML docs, samples, packages, ADRs, CHANGELOG, STATE, and one squash commit on `main`.

---

## Feature FF-F01 — Secure and Reliable Foundations

**Objective:** Establish the engineering platform and deliver Security, Storage, Integrity, and Observability packages; harden for v0.5.0.

### PBI-01 — Repository and engineering foundation

**Objective:** SDK pinning, build/analysis/CI, packaging conventions, docs, ADR template, minimal pipeline smoke package (removed/converted before completion).

**Dependencies:** None.

**Acceptance criteria:**

* Restore, build, test, and pack succeed from a clean checkout.
* CI executes the same commands.
* Missing XML documentation fails the build.
* Package artifacts are reproducible to the practical extent supported by the SDK.
* No production utility functions added merely for scaffolding.

**Tasks:**

| ID | Task |
|---|---|
| T01-01 | Pin SDK via `global.json`; document explicit maintenance upgrades |
| T01-02 | Create `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, solution |
| T01-03 | Repository docs: README, CONTRIBUTING, SECURITY, LICENSE, CHANGELOG |
| T01-04 | ADR template and foundational ADRs (modular design, dependency policy, SDK policy) |
| T01-05 | Test and benchmark conventions; smoke library + unit test |
| T01-06 | GitHub Actions CI quality gates |
| T01-07 | Package validation and Source Link configuration |
| T01-08 | Convert/remove smoke package; finalize STATE/ROADMAP |

**Expected public APIs:** None lasting (smoke package only during scaffolding).

**Test strategy:** Smoke unit test proving test host; docs example compilation gate later.

**Benchmark strategy:** Benchmark project template; no meaningful production benchmarks yet.

**Final main commit:** `build(repo): establish FunctionFoundry engineering foundation [PBI-01]`

**Technical risks:** SDK 10 availability on CI runners; package validation baseline may be empty until first release.

---

### PBI-02 — Security package

**Objective:** Deliver `FunctionFoundry.Security` with envelope encryption, deterministic pseudonymization, Shamir secret sharing, and key-rotation planning.

**Dependencies:** PBI-01.

**Acceptance criteria:** All §7.1 capabilities; security misuse/malformed tests; zero third-party runtime deps; XML docs; samples; benchmarks for crypto hot paths.

**Tasks:**

| ID | Task |
|---|---|
| T02-01 | Package scaffold + threat model ADR |
| T02-02 | Versioned AES-GCM envelope encryption |
| T02-03 | Pluggable key resolution and rotation-aware decrypt |
| T02-04 | Deterministic HMAC pseudonymization |
| T02-05 | Shamir threshold secret sharing |
| T02-06 | Key rotation planning |
| T02-07 | Misuse/malformed/oversized/tampering tests |
| T02-08 | Sample + benchmarks + package docs |

**Expected public APIs:** `EnvelopeEncryptor`, `Pseudonymizer`, `SecretSharer`, `KeyRotationPlanner`, options/result types, key resolver abstractions.

**Test strategy:** Vectors for AES-GCM and Shamir; constant-time compare tests; no plaintext on auth failure.

**Benchmark strategy:** Encrypt/decrypt throughput; share split/combine; pseudonym generate/verify.

**Final main commit:** `feat(security): deliver advanced security functions [PBI-02]`

**Technical risks:** Constant-time claims on managed runtimes; share field arithmetic correctness.

---

### PBI-03 — Storage package

**Objective:** Deliver `FunctionFoundry.Storage` transactional writer, CAS, CDC chunking, Merkle file trees.

**Dependencies:** PBI-01.

**Acceptance criteria:** All §7.2 capabilities; crash/recovery tests; no trivial File API wrappers.

**Tasks:** T03-01..T03-08 covering scaffold, transactional writer, recovery, CAS, chunking, Merkle diff, concurrency/crash tests, samples/benchmarks.

**Expected public APIs:** `TransactionalFileSetWriter`, `ContentAddressedStore`, `ContentDefinedChunker`, `MerkleFileTree`.

**Final main commit:** `feat(storage): deliver transactional and content-addressed storage [PBI-03]`

**Technical risks:** OS atomic replace differences; concurrent CAS writers.

---

### PBI-04 — Integrity package

**Objective:** Deliver `FunctionFoundry.Integrity` canonical JSON, hash manifests, Merkle trees/proofs, hash chains.

**Dependencies:** PBI-01.

**Acceptance criteria:** All §7.3 capabilities; conformance vectors where available.

**Tasks:** T04-01..T04-08 for each capability + docs/tests/benchmarks.

**Expected public APIs:** `CanonicalJson`, `HashManifestBuilder`, `MerkleTree`, `HashChain`.

**Final main commit:** `feat(integrity): deliver canonicalization and verifiable structures [PBI-04]`

**Technical risks:** Choosing/documenting a canonical JSON profile; Unicode edge cases.

---

### PBI-05 — Observability package

**Objective:** Deliver `FunctionFoundry.Observability` redaction, fingerprinting, adaptive sampling, burst coalescing.

**Dependencies:** PBI-01.

**Acceptance criteria:** All §7.4 capabilities; logging-framework-neutral core; optional MEL adapter only if justified by ADR.

**Tasks:** T05-01..T05-08.

**Expected public APIs:** `SensitiveDataRedactor`, `EventFingerprinter`, `AdaptiveSampler`, `BurstCoalescer`.

**Final main commit:** `feat(observability): deliver adaptive and privacy-aware log processing [PBI-05]`

**Technical risks:** Reflection safety for redaction; false-positive secret detection.

---

### PBI-06 — Feature 1 hardening (v0.5.0)

**Objective:** Cross-package review, docs/threat/API audits, benchmarks/size reports, samples, v0.5.0 artifacts.

**Dependencies:** PBI-02..PBI-05.

**Acceptance criteria:** Hardened packages; version prefix 0.5.0; no aggregate assembly; release notes.

**Tasks:** T06-01..T06-08 audit, samples, benchmarks, pack, tag if permitted.

**Final main commit:** `chore(release): harden foundational packages for v0.5.0 [PBI-06]`

---

## Feature FF-F02 — Advanced Processing and Coordination

**Objective:** Deliver Data, Text, Resilience, Networking, Distributed, Scheduling; harden for v1.0.0.

### PBI-07 — Data package

**Capabilities:** External merge sort, structural tree diff, three-way merge, temporal interval join.

**Final main commit:** `feat(data): deliver bounded-memory and conflict-aware processing [PBI-07]`

**Tasks:** T07-01..T07-08.

### PBI-08 — Text package

**Capabilities:** Spoof detection, secret scanner, near-duplicate index, dialect inference.

**Final main commit:** `feat(text): deliver spoof detection and approximate text analysis [PBI-08]`

**Tasks:** T08-01..T08-08.

### PBI-09 — Resilience package

**Capabilities:** Adaptive concurrency, hedged execution, execution budget, checkpointed batch executor.

**Final main commit:** `feat(resilience): deliver adaptive execution mechanisms [PBI-09]`

**Tasks:** T09-01..T09-08.

### PBI-10 — Networking package

**Capabilities:** Resumable parallel downloader, mirror selector, transfer plan, streaming integrity verifier.

**Final main commit:** `feat(networking): deliver resumable and integrity-aware transfers [PBI-10]`

**Tasks:** T10-01..T10-08.

### PBI-11 — Distributed package

**Capabilities:** Weighted Rendezvous, version clocks, phi-accrual detector, quorum aggregator.

**Final main commit:** `feat(distributed): deliver reusable coordination algorithms [PBI-11]`

**Tasks:** T11-01..T11-08.

### PBI-12 — Scheduling package and v1.0 hardening

**Capabilities:** Interval-set algebra, recurring availability, business calendar, critical-path scheduler; full product hardening; v1.0.0 artifacts.

**Final main commit:** `feat(scheduling): complete FunctionFoundry v1.0 [PBI-12]`

**Tasks:** T12-01..T12-10 (implement scheduling + audits + pack + verification).

---

## Cross-cutting policies

* **Runtime dependencies:** Zero third-party by default; ADR required for any exception.
* **Inter-package refs:** Forbidden for core packages in v1.0.
* **Documentation language:** English only.
* **Coverage:** ≥85% line; ≥90% branch for critical algorithms (justified exclusions documented).
* **Versioning:** SemVer; Feature 1 → v0.5.0; Feature 2 → v1.0.0.
* **Reference repo:** Chookees/FunctionProvider consulted only as a high-level idea reference; no code, APIs, names, or structure copied (ADR-0002).
