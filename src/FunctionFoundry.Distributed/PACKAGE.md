# FunctionFoundry.Distributed

Production-grade distributed coordination primitives with deterministic behavior and explicit non-guarantees.

## Capabilities

* **Weighted rendezvous hashing** — stable, weight-aware node selection with optional bounded-load enforcement
* **Version clocks** — vector and dotted clocks with merge, dominance, and compaction policies
* **Hybrid logical clocks** — physical/logical timestamps for causal ordering across loosely synchronized nodes
* **Phi-accrual failure detector** — heartbeat-driven suspicion scoring with injectable clocks and warm-up
* **Quorum result aggregation** — early completion, conflict detection, and cancellation of unnecessary work

## Runtime dependencies

None beyond the .NET shared framework.

## Non-guarantees

Phi-accrual suspicion is statistical evidence, not proof of failure. Quorum aggregation does not imply linearizability unless the host protocol provides it.
