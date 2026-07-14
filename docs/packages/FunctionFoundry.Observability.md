# FunctionFoundry.Observability

Logging-framework-neutral observability primitives for privacy-aware log processing.

## Components

### SensitiveDataRedactor

Structural redaction over dictionaries, collections, and CLR object graphs:

* Property-name policies (`password`, `token`, `api_key`, …)
* Regex pattern rules (Bearer tokens, JWTs, emails, PAN-like numbers, AWS key ids)
* Entropy-based secret candidate detection
* Replacement strategies: redacted token, masked, hashed prefix, removed
* Depth and visit limits with cycle detection

Property getters may run during reflection traversal; prefer dictionary-shaped payloads when getters have side effects.

### EventFingerprinter

Stable SHA-256 fingerprints with explainable components:

* Strips GUIDs, timestamps, memory addresses, and correlation tokens from messages
* Normalizes exception stacks (file names only, no line numbers)
* Preserves causal exception chain ordering

### AdaptiveSampler

Severity-aware sampling with bounded fingerprint memory:

* `Error` and above always retained by default
* Deterministic per-fingerprint pass/drop decisions
* Global rate adapts to volume and cardinality
* Exportable `AdaptiveSamplerStats`

### BurstCoalescer

Time-window burst aggregation:

* Preserves first and last payloads
* Retains representative samples (bounded per burst)
* Reports dropped counts
* Bounded fingerprint cardinality with eviction

## Non-goals

* Microsoft.Extensions.Logging adapters (integrators wire primitives themselves)
* OpenTelemetry protocol exporters
* Persistent storage of fingerprints or bursts

## Algorithm references

* Shannon entropy for secret-likeness heuristics
* SHA-256 for fingerprinting and hashed redaction tokens
