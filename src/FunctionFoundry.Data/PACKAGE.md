# FunctionFoundry.Data

Bounded-memory and conflict-aware data processing primitives.

## Capabilities

* **Bounded-memory external merge sort** — generic records, stable option, memory budget, temp runs, k-way merge, cancel, cleanup, recovery diagnostics for abandoned temps
* **Structural tree diff** — objects/arrays/scalars; add/remove/replace/move; identity keys for arrays; deterministic ops; complexity limits; human+machine readable
* **Three-way merge** — base/local/remote; explicit conflicts; policies; no silent loss; path-level conflicts; deterministic
* **Temporal interval join** — overlapping validity intervals; open/closed bounds; streaming/sorted mode; bounded memory; invalid interval handling
* **K-way sorted merge** — heap merge of already-sorted sequences with optional adjacent deduplication

## Runtime dependencies

None beyond the .NET shared framework.

## Non-goals

* Distributed query execution or SQL translation
* Automatic conflict resolution without explicit policy
* Full JSON Schema validation
