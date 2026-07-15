# FunctionFoundry.Distributed

Reusable distributed-systems algorithms without requiring a network framework.

## What this product does

It provides **membership hashing, causality tracking, failure suspicion, and quorum aggregation** as pure in-process building blocks.

### 1. Weighted rendezvous hashing (`WeightedRendezvousHasher`)

Picks stable nodes for keys with:

* node weights
* optional bounded-load behavior
* deterministic hashing
* minimal remapping when membership changes

### 2. Vector and dotted version clocks (`VectorClock` / `DottedVersionClock`)

Tracks causal history across replicas:

* increment, merge, dominance comparison
* concurrent-version detection
* deterministic serialization
* configurable growth limits and compaction trade-offs

### 3. Phi-accrual failure detector (`PhiAccrualFailureDetector`)

Computes a continuous **suspicion score** from heartbeat intervals:

* sample window and warm-up behavior
* injectable clock abstraction
* outlier handling
* deterministic simulations for tests

Suspicion is **not** proof of failure.

### 4. Quorum result aggregator (`QuorumResultAggregator`)

Collects concurrent replica results under a quorum policy:

* agreement and conflict detection
* early completion
* failure evidence
* cancellation of unnecessary work
* deterministic winner selection only when the policy allows it

## When to use it

* Client-side shard / replica selection
* Conflict detection for multi-writer records
* Heartbeat-based soft failure detection
* Reading from N replicas and waiting for quorum

## Non-goals

* Shipping a cluster membership protocol or RPC stack
* Treating phi scores as hard failovers without host policy

## Install

```bash
dotnet add package FunctionFoundry.Distributed
```

## Runtime dependencies

None beyond the .NET shared framework.
