# Contributing to FunctionFoundry

Thank you for contributing. This repository builds specialized .NET libraries, not a generic utility bag.

## Development setup

1. Install .NET SDK **10.0.301** exactly (see `global.json`).
2. Restore, build, and test:

```bash
dotnet restore
dotnet build -c Release
dotnet test -c Release --collect:"XPlat Code Coverage"
dotnet pack -c Release
```

## Coding standards

* Target `net10.0`, C# 14, nullable enabled, warnings as errors.
* Public APIs require high-quality English XML documentation.
* Do not add wrappers around trivial BCL APIs.
* Core packages must not depend on other FunctionFoundry packages in v1.0.
* Do not introduce `FunctionFoundry.All`, `.Common`, `.Utils`, or `.Helpers`.
* Every runtime third-party dependency needs an ADR.

## Pull requests

1. Create a branch per PBI: `pbi/<pbi-id>-<short-description>` (cloud agents may use `cursor/pbi-<id>-...-482e`).
2. Make atomic Task-sized commits that build and pass tests.
3. Update `CHANGELOG.md` and `STATE.md`.
4. Ensure CI quality gates pass.

## Rejection checklist

Before adding a public function, confirm it is not a renamed BCL call, not implementable correctly in ~5 lines by a caller, and that its edge cases, performance, and security consequences can be explained honestly.

## Security

Report vulnerabilities privately per [SECURITY.md](SECURITY.md). Do not open public issues for undisclosed security defects.
