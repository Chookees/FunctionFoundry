# FunctionFoundry.Resilience

Adaptive execution mechanisms that go beyond ordinary retries, timeouts, and circuit breakers.

## What this product does

It controls **how much work runs, when secondary copies start, how shared deadlines are split, and how large batches resume safely**.

### 1. Adaptive concurrency controller (`AdaptiveConcurrencyController`)

AIMD-style concurrency regulation driven by latency and error feedback:

* configurable min/max concurrency
* warm-up behavior
* thread-safe acquisition and queue limits
* exportable state snapshots
* deterministic simulation hooks for tests

### 2. Hedged execution (`HedgedExecution`)

Starts secondary attempts based on rolling latency percentiles:

* shared cancellation; first valid result wins
* failure aggregation
* maximum amplification limits
* idempotency warnings and metrics for extra work

Use only when the operation is safe to duplicate.

### 3. Execution budget (`ExecutionBudget`)

Allocates one overall deadline across multiple steps:

* reserve cleanup time
* rebalance unused budget
* reject impossible / negative allocations
* nested budgets
* monotonic time sources

### 4. Checkpointed batch executor (`CheckpointedBatchExecutor`)

Processes large batches with bounded concurrency:

* persists successful item checkpoints through an abstraction
* resumes safely after interruption
* records partial failures
* requires deterministic item ids and explicit idempotency
* no hidden infinite retries

## When to use it

* Client libraries calling overloaded backends
* Fan-out calls where hedging reduces tail latency
* Multi-step workflows with a hard end-to-end deadline
* Overnight batches that must restart mid-way

## Non-goals

* Replacing Polly-style basic retry / circuit-breaker APIs
* Making non-idempotent APIs safe to hedge automatically

## Install

```bash
dotnet add package FunctionFoundry.Resilience
```

## Runtime dependencies

None beyond the .NET shared framework.
