# FunctionFoundry.Distributed

## Coordination primitives

### Weighted rendezvous hashing

Deterministic node ranking with weight-aware scores and optional bounded-load enforcement to reduce hot spots.

### Version clocks

Vector and dotted clocks support increment, merge, dominance checks, growth limits, and compaction with documented causal trade-offs.

### Phi-accrual failure detector

Heartbeat-driven suspicion scoring with injectable clocks, warm-up windows, and outlier filtering. Suspicion is evidence, not proof.

### Quorum aggregation

Replica result collection with early completion, conflict reporting, failure evidence, and deterministic tie-breaking only when explicitly allowed.
