# FunctionFoundry.Resilience

## Design notes

### Adaptive concurrency

Uses additive-increase / multiplicative-decrease (AIMD) driven by observed latency relative to a target and error-rate thresholds. Warm-up starts at a configured concurrency before adaptation begins. Queue depth is bounded; callers receive explicit backpressure when limits are reached.

### Hedged execution

Secondary attempts start after a delay derived from rolling latency percentiles. The first successful, valid result wins; remaining attempts are cancelled via a shared token. Amplification is capped and non-idempotent operations emit warnings unless explicitly acknowledged.

### Execution budgets

Deadlines are tracked with monotonic timestamps (`Stopwatch`) so wall-clock adjustments do not extend or shrink remaining budget unexpectedly. Nested child budgets cannot exceed parent reservations; unused time can be rebalanced on release.

### Checkpointed batch execution

Checkpoints are persisted through `ICheckpointStore` with deterministic item identifiers. Failed items are recorded explicitly; the executor does not perform unbounded automatic retries.
