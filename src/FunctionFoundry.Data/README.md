# FunctionFoundry.Data

Bounded-memory and conflict-aware data processing for hard cases the BCL does not solve as a reusable package.

## What this product does

It handles **large sorts, structured diffs, merges with conflicts, and temporal overlaps** with explicit limits and deterministic outputs.

### 1. Bounded-memory external merge sort (`BoundedMemoryExternalMergeSort`)

Sorts generic records that do not fit in memory:

* configurable memory budget
* temporary run files and k-way merge
* optional stable ordering
* cancellation and cleanup after success, cancel, or failure
* diagnostics for abandoned temporary data

### 2. Structural tree diff (`StructuralTreeDiff`)

Diffs nested objects, arrays, and scalars into ordered operations:

* add / remove / replace / move
* configurable identity keys for array elements
* complexity limits against hostile inputs
* human-readable and machine-readable results

### 3. Three-way merge (`ThreeWayMerge`)

Merges **base + local + remote** values with:

* explicit conflict objects (no silent conflict loss)
* configurable merge policies
* path-level conflict reporting
* deterministic output ordering

### 4. Temporal interval join (`TemporalIntervalJoin`)

Joins records whose validity intervals overlap:

* open / closed boundary policies
* sorted-input friendly processing
* bounded buffering where possible
* clear handling of invalid intervals

## When to use it

* Offline ETL or analytics sorts larger than RAM
* Document / config sync that must show structural changes
* CRDT-adjacent or collaborative edits needing honest conflicts
* Validity-window joins (pricing, entitlements, employment periods)

## Non-goals

* A general query engine or ORM
* Silently “winning” merges by last-write-wins defaults

## Install

```bash
dotnet add package FunctionFoundry.Data
```

## Runtime dependencies

None beyond the .NET shared framework.
