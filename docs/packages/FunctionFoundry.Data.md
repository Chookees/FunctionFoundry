# FunctionFoundry.Data

## Threat model

### Assets

* Source record streams and sorted output
* Temporary run files on local disk
* Merge inputs (base/local/remote document trees)

### Adversaries

* Process crashes leaving abandoned temp runs
* Callers supplying oversized trees or unbounded streams
* Cancellation during long-running sorts or joins

### Guarantees

* External merge sort respects memory budgets and cleans up temp runs on success
* Abandoned temp diagnostics are deterministic and do not delete files automatically
* Three-way merge surfaces all path-level conflicts; no silent data loss
* Structural diff operations are ordered deterministically
* Interval joins reject invalid intervals explicitly

### Non-goals

* Cross-machine merge coordination
* Encryption of temp files (see FunctionFoundry.Security)
* Heuristic automatic conflict resolution

## Algorithm references

* External merge sort (Knuth, TAOCP Vol. 3)
* Myers diff lineage for tree-structured documents
* Three-way merge (Fowler/Noll/Vo style path conflicts)
* Interval join (Allen interval algebra)
