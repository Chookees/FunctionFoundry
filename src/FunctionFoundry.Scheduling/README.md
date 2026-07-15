# FunctionFoundry.Scheduling

Advanced interval, calendar, and dependency scheduling.

## What this product does

It computes **when things can happen** using interval algebra, calendars, recurring availability, and critical-path analysis.

### 1. Interval-set algebra (`IntervalSetAlgebra`)

Exact set operations on intervals:

* union, intersection, difference
* complement within a boundary
* open and closed endpoints
* normalization and deterministic ordering
* efficient handling of large sorted interval sets

### 2. Recurring availability resolver (`RecurringAvailabilityResolver`)

Finds meeting / slot candidates across participants:

* time zones and DST transitions
* recurring local-time windows and exceptions
* minimum duration constraints
* ranked candidate windows
* explicit policies for ambiguous / invalid local times

### 3. Business calendar (`BusinessCalendar`)

Working-time math without a hard-coded global holiday database:

* working days and split working hours
* holidays / closures supplied as region-independent inputs
* add or subtract working duration
* explainable calculation path

### 4. Critical-path scheduler (`CriticalPathScheduler`)

Schedules dependency graphs:

* cycle validation with an actual reported cycle
* earliest / latest start, slack, and critical path
* optional resource-capacity heuristic
* clearly marks when a heuristic result is not globally optimal

## When to use it

* Calendar math for SLAs and maintenance windows
* Multi-timezone meeting finders
* Project / workflow planning with dependencies
* Working-hours deadline calculation

## Non-goals

* Embedding a complete worldwide holiday catalog
* Guaranteeing globally optimal resource leveling when heuristics are used

## Install

```bash
dotnet add package FunctionFoundry.Scheduling
```

## Runtime dependencies

None beyond the .NET shared framework.
