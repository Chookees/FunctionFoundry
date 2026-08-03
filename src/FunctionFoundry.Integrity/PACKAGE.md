# FunctionFoundry.Integrity

Deterministic integrity and verifiable-structure primitives over BCL cryptography and UTF-8 processing.

## Capabilities

* **Canonical JSON** — documented deterministic JSON profile with sorted object keys, strict number rules, Unicode handling, duplicate-property rejection, and streaming canonicalization
* **Streaming hash manifests** — multi-file/stream manifests with SHA-256 (SHA-384 optional), deterministic serialization, path-traversal protection, and detailed verification reports
* **Merkle trees** — deterministic leaf encoding, inclusion proofs, multiproofs, odd-node duplication, and domain-separated leaf vs internal nodes
* **Hash chains** — append-only record chaining with sequence/timestamp policy, canonical encoding, and first-invalid-record verification reports

## Runtime dependencies

None beyond the .NET shared framework.

## Important limitations

Hash chains provide tamper-evidence over record ordering and payload binding. They do **not** provide trusted timestamping or non-repudiation against a motivated clock forger. See XML remarks on <xref href="FunctionFoundry.Integrity.HashChain"/>.
