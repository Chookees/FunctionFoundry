# FunctionFoundry State

Resume this file instead of conversation context.

## Current Feature

FF-F01 — Secure and Reliable Foundations

## Current PBI

PBI-01 complete; preparing squash to main, then PBI-02.

## Completed Tasks

* T01-01 Pin SDK via `global.json` (`rollForward: disable`)
* T01-02 Directory.Build.props/targets, Directory.Packages.props, .editorconfig, FunctionFoundry.slnx
* T01-03 README, CONTRIBUTING, SECURITY, LICENSE, CHANGELOG
* T01-04 ADR template + ADR-0001..0004
* T01-05 Test conventions; temporary smoke package proved pipeline then removed; Engineering.Tests remain
* T01-06 GitHub Actions CI workflow
* T01-07 Package validation / Source Link config in Directory.Build.props
* T01-08 Smoke package removed; STATE/ROADMAP finalized for PBI-01

## Remaining Tasks

* Squash PBI-01 to main
* Begin PBI-02 Security package

## Last successful commands

* `dotnet --version` → `10.0.301`
* `dotnet restore`
* `dotnet build -c Release` (0 warnings, 0 errors)
* `dotnet test -c Release` (3 passed)
* `dotnet pack -c Release` (no packable projects yet — expected)
* `dotnet format --verify-no-changes`
* Verified CS1591 fails build when public XML docs missing

## Last successful commit

* (pending PBI-01 squash)

## Open technical risks

* No packable library yet until PBI-02
* Package validation baseline starts with first released packages

## Decisions made

* Prefix `FunctionFoundry` (ADR-0001)
* Modular independent libraries; no FunctionProvider copy (ADR-0002)
* SDK 10.0.301 pinned (ADR-0003)
* Zero third-party runtime deps by default (ADR-0004)
* Smoke package removed after pipeline proof; engineering tests validate pinning/docs settings

## Known deviations

* Cloud agent PBI branches use `cursor/pbi-XX-...-482e` naming required by the agent environment while retaining squash-to-main PBI workflow.

## Exact next action

Commit PBI-01 tasks, squash to main, start PBI-02 Security implementation.
