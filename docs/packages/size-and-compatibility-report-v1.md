# Package size and compatibility report (v1.0.0)

Generated from local Release build artifacts on 2026-07-14.

## NuGet package sizes (`.nupkg`)

| Package | Bytes |
|---|---|
| FunctionFoundry.Security | 21439 |
| FunctionFoundry.Distributed | 26988 |
| FunctionFoundry.Text | 27854 |
| FunctionFoundry.Networking | 28349 |
| FunctionFoundry.Integrity | 30363 |
| FunctionFoundry.Scheduling | 31214 |
| FunctionFoundry.Observability | 33014 |
| FunctionFoundry.Resilience | 33921 |
| FunctionFoundry.Data | 40371 |
| FunctionFoundry.Storage | 47702 |

## DLL sizes (Release `net10.0`)

| Assembly | Bytes |
|---|---|
| FunctionFoundry.Security.dll | 29184 |
| FunctionFoundry.Distributed.dll | 42496 |
| FunctionFoundry.Integrity.dll | 48640 |
| FunctionFoundry.Text.dll | 48640 |
| FunctionFoundry.Networking.dll | 50176 |
| FunctionFoundry.Observability.dll | 55808 |
| FunctionFoundry.Scheduling.dll | 58368 |
| FunctionFoundry.Resilience.dll | 63488 |
| FunctionFoundry.Data.dll | 74752 |
| FunctionFoundry.Storage.dll | 94720 |

## Runtime dependencies

All ten core packages: **zero** third-party runtime NuGet dependencies (BCL only).

## Independence

* No `FunctionFoundry.All` / `.Common` / `.Utils` / `.Helpers` packages.
* No ProjectReference between core `src/FunctionFoundry.*` packages.
* Each package includes DLL + XML documentation + PACKAGE.md readme + symbols.

## Trimming / AOT

`IsTrimmable` / `IsAotCompatible` remain unset/`false` until dedicated trimmed smoke apps are promoted. Packages avoid unbounded reflection except Observability redaction (explicit depth/item limits documented).

## Notes

* Feature 1 conceptual baseline corresponds to Security/Storage/Integrity/Observability (historically labeled v0.5.0 in the roadmap).
* Feature 2 completion ships as **v1.0.0** package artifacts locally. NuGet.org publish and git tags are performed only when permissions allow.
