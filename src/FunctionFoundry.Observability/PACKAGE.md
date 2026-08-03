# FunctionFoundry.Observability

Logging-framework-neutral observability primitives for privacy-aware log processing.

## Capabilities

* **Sensitive-data redaction** — structural traversal with property-name policies, pattern rules, entropy detection, and replacement strategies
* **Event fingerprinting** — stable hashes from normalized messages and exception stacks with explainable components
* **Adaptive sampling** — severity-aware, fingerprint-stable sampling with bounded memory and exportable statistics
* **Burst coalescing** — time-window aggregation of repeated events with first/last preservation and representative samples
* **Cardinality limiter** — bounded distinct-key admission per telemetry dimension with overflow bucketing

## Runtime dependencies

None beyond the .NET shared framework.

## Design notes

This package does not reference Microsoft.Extensions.Logging or other logging frameworks. Integrators wire these primitives into their chosen logging pipeline.
