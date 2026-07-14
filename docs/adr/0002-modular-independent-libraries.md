# ADR-0002: Independent modular libraries (not FunctionProvider-style)

* **Status:** Accepted
* **Date:** 2026-07-14
* **Deciders:** FunctionFoundry architects

## Context

The GitHub repository Chookees/FunctionProvider was inspected only as a high-level reference for the general idea of reusable functions. That reference tends toward a broad, monolithic helper collection.

FunctionFoundry must not become a generic utility bag and must remain independently consumable.

## Decision

* Ship multiple focused DLLs and NuGet packages; no mandatory aggregate package.
* Forbid `FunctionFoundry.All`, `.Common`, `.Utils`, and `.Helpers` runtime packages.
* Core packages in v1.0 must not reference other FunctionFoundry packages.
* Do not copy FunctionProvider source, APIs, class names, project structure, documentation, tests, examples, or naming patterns.
* Prefer specialized algorithms and protocols with explicit guarantees over convenience wrappers.

## Consequences

* Callers take only the packages they need.
* Small algorithm duplication across packages is allowed when better than a mandatory shared runtime dependency.
* Slightly more repository surface area (per-package tests/samples/docs).

## Alternatives considered

* Single monolithic DLL — rejected (forces unused surface and couples versioning).
* Shared `FunctionFoundry.Common` runtime — rejected (creates mandatory transitive dependency and becomes a dumping ground).
