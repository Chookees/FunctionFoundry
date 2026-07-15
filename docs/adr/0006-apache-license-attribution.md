# ADR-0006: Apache License 2.0 with attribution NOTICE

* **Status:** Accepted
* **Date:** 2026-07-15
* **Deciders:** FunctionFoundry maintainers

## Context

The project previously used the MIT License. The maintainers want a license that remains free for commercial and non-commercial use while requiring clear attribution when FunctionFoundry is used or redistributed.

## Decision

Adopt the **Apache License, Version 2.0** and ship a repository `NOTICE` file that names FunctionFoundry for attribution. NuGet packages use `PackageLicenseExpression=Apache-2.0` and include the NOTICE file.

## Consequences

* Users may use, modify, and redistribute FunctionFoundry freely at no charge.
* Redistributors must retain copyright/attribution notices and the NOTICE content as required by Apache-2.0.
* An express patent grant is included (Apache-2.0 advantage over MIT).
* Existing MIT references and badge text are replaced.

## Alternatives considered

* Remain on MIT — still free, but attribution wording is weaker/less prominent for this goal.
* BSD-3-Clause — also attribution-friendly; Apache-2.0 preferred for NOTICE convention and patent grant.
* CC BY 4.0 — strong attribution, but unsuitable as a primary software license.
