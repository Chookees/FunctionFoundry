# FunctionFoundry.Resilience

Adaptive execution orchestration without reimplementing basic retry, circuit-breaker, or token-bucket primitives.

## Capabilities

* **Adaptive concurrency** — AIMD controller with latency and error feedback, warm-up, queue limits, and exportable snapshots
* **Hedged execution** — percentile-driven secondary attempts with shared cancellation, amplification limits, and idempotency warnings
* **Execution budgets** — monotonic deadlines with nested reservations, cleanup, and impossible-allocation prevention
* **Checkpointed batch execution** — bounded concurrency with explicit checkpoint persistence, resume, and partial-failure reporting
* **Retry budget** — token-budget gate that admits retries without amplifying outages

## Runtime dependencies

None beyond the .NET shared framework.

## Non-goals

* Generic retry policies, circuit breakers, or rate limiters (use established libraries at the application layer)
* Hidden infinite retries or implicit idempotency assumptions
