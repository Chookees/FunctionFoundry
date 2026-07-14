# ADR-0004: Runtime dependency budget

* **Status:** Accepted
* **Date:** 2026-07-14
* **Deciders:** FunctionFoundry architects

## Context

Third-party runtime dependencies enlarge attack surface, licensing review load, package size, and binding risk.

## Decision

* Default budget: **zero** third-party runtime dependencies per core package.
* BCL / shared framework dependencies are expected.
* At most one third-party runtime dependency may be introduced when reimplementing would be unsafe, misleading, or irresponsible, and only with a dedicated ADR covering justification, size, security, license, transitive graph, and alternatives.
* Test, analyzer, coverage, benchmark, and documentation tools do not count as runtime dependencies.

## Consequences

* Prefer BCL cryptography, HTTP, JSON, and concurrency primitives.
* More in-house algorithm code with stronger ownership of correctness.

## Alternatives considered

* Allow common helper libraries freely — rejected (violates specialization and size goals).
