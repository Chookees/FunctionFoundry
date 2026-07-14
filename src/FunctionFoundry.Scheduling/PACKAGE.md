# FunctionFoundry.Scheduling

Deterministic scheduling primitives for intervals, recurring availability, business-time arithmetic, and critical-path analysis.

## Capabilities

* **Interval-set algebra** — normalized union, intersection, difference, and complement on open/closed bounds
* **Recurring availability** — multi-participant, time-zone-aware windows with DST policies and ranked candidates
* **Business calendar** — working-day arithmetic with split hours and caller-supplied closures
* **Critical-path scheduler** — dependency validation, cycle reporting, slack analysis, and optional resource heuristics

## Runtime dependencies

None beyond the .NET shared framework.

## Non-goals

No built-in global holiday database. Resource leveling heuristics are explicitly marked non-optimal.
