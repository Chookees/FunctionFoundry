# FunctionFoundry.Observability

Advanced processing for structured logs and diagnostics — without forcing a logging framework.

## What this product does

It transforms diagnostic events for **privacy, grouping, and volume control** before they hit expensive storage or sinks.

### 1. Sensitive-data redactor (`SensitiveDataRedactor`)

Walks nested objects, dictionaries, and collections (with cycle protection) and redacts:

* property-name policies (`password`, `token`, …)
* pattern-based values
* high-entropy secret candidates

Supports replacement strategies and hard limits on depth / item counts so inspection stays bounded. Designed to avoid arbitrary user-code invocation where practical.

### 2. Deterministic event fingerprinting (`EventFingerprinter`)

Normalizes exception stacks and strips unstable values (timestamps, GUIDs, correlation noise) while keeping causal structure. Equivalent failures get a **stable fingerprint** plus an explanation of which normalized parts contributed.

### 3. Adaptive deterministic sampling (`AdaptiveSampler`)

Decides whether to keep an event based on fingerprint and observed volume:

* always preserves high-severity events
* identical fingerprints get stable decisions
* adapts rates under high cardinality / volume
* bounded memory, thread-safe, exportable stats

### 4. Burst coalescing (`BurstCoalescer`)

Merges repeated events inside a time window:

* keeps first and last occurrence
* keeps representative samples
* emits dropped / coalesced counts
* handles concurrent producers without unbounded cardinality growth

## When to use it

* Log pipelines that must strip secrets before export
* Alert grouping / error fingerprinting across instances
* Cost control when a single bug floods telemetry

## Non-goals

* Being a full logging framework (Serilog/MEL replacement)
* Guaranteeing zero secret leakage for all custom objects

## Install

```bash
dotnet add package FunctionFoundry.Observability
```

## Runtime dependencies

None beyond the .NET shared framework. Logging-framework-neutral by design.
