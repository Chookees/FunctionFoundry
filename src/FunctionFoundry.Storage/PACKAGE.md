# FunctionFoundry.Storage

Reliable local persistence primitives for atomic multi-file updates, content-addressed blobs, content-defined chunking, and Merkle directory snapshots.

## Capabilities

* **Transactional file-set writer** — staging directory, write-ahead journal, atomic replace with explicit fallback, rollback, crash recovery, per-file SHA-256 checksums, deterministic recovery reports
* **Content-addressed store** — streaming SHA-256 ingestion, deduplication, integrity verification, safe concurrent writers, orphan detection, GC planning and execution
* **Content-defined chunker** — Rabin-style rolling-hash CDC with configurable min/target/max sizes and bounded memory
* **Merkle file tree** — deterministic directory snapshots, add/remove/modify/moved detection, symlink policy, cycle protection, cross-platform path normalization

## Runtime dependencies

None beyond the .NET shared framework.

## Operational notes

Crash recovery replays or rolls back incomplete transactions using the write-ahead journal. Content-addressed GC planning reports reclaimable objects but never deletes data automatically. Merkle snapshots normalize paths with forward slashes for cross-platform comparison.
