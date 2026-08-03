# FunctionFoundry State

Resume this file instead of conversation context.

## Current Feature

FunctionFoundry **v1.1.0** capabilities on branch `feat/v1.1.0-capabilities`.

## Completed Tasks

* PBI-01..PBI-12 (v1.0)
* Health-scan remediation merged via PR #8
* v1.1.0 APIs: HKDF deriver, CAS GC execute, Merkle multiproof, HLC, homoglyph normalizer, cardinality limiter, retry budget, working-duration measure, k-way merge, Content-Range parser

## Remaining Tasks

* Publish NuGet `1.1.0` (requires `NUGET_API_KEY`); optionally publish missing clean `1.0.0`
* Optional: Native AOT/trimming smoke before claiming `IsAotCompatible`
* Optional: `PackageValidationBaselineVersion` after clean NuGet baseline exists

## Exact next action

Land v1.1.0 on `main`, tag `1.1.0`, run Publish NuGet workflow.
