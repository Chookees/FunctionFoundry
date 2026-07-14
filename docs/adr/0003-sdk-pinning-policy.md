# ADR-0003: Pin .NET SDK 10.0.301 with explicit upgrades

* **Status:** Accepted
* **Date:** 2026-07-14
* **Deciders:** FunctionFoundry architects

## Context

Silent SDK roll-forward can change analyzer behavior, pack outputs, and language features without a deliberate review.

## Decision

* Pin SDK `10.0.301` in `global.json`.
* Set `rollForward` to `disable`.
* Target `net10.0` and C# 14.0.
* Treat future SDK moves as explicit maintenance upgrades with changelog notes and CI verification.

## Consequences

* Reproducible local and CI builds when the pinned SDK is installed.
* Contributors must install the exact SDK version.

## Alternatives considered

* `rollForward: latestFeature` — rejected (silent band movement).
* Floating latest SDK — rejected (non-reproducible builds).
