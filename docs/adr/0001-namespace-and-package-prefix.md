# ADR-0001: FunctionFoundry namespace and package prefix

* **Status:** Accepted
* **Date:** 2026-07-14
* **Deciders:** FunctionFoundry architects

## Context

The product needs a stable root namespace and NuGet package prefix. A collision with an existing published package would force a rename.

## Decision

Use `FunctionFoundry` as the root namespace and package id prefix for all core libraries (`FunctionFoundry.Security`, etc.).

At project start, no conflicting published package requiring a rename was identified. If a collision is later discovered, select a professional alternative and supersede this ADR.

## Consequences

* Consistent branding and documentation.
* Independent NuGet ids per library under one prefix.

## Alternatives considered

* Shorten to `Foundry.*` — weaker brand clarity.
* Use company/user prefix only — less portable for open collaboration.
