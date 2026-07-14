# FunctionFoundry.Storage

## Threat model

### Assets

* Target file sets and staged transaction data
* Content-addressed object blobs keyed by SHA-256
* Merkle snapshot metadata describing directory contents

### Adversaries

* Process crashes or power loss during commit
* Concurrent writers racing to store identical content hashes
* Symlink cycles or path tricks during directory walks
* Callers supplying invalid paths, streams, or cancellation during long operations

### Guarantees

* Incomplete transactions are recoverable via the write-ahead journal with deterministic reports
* Per-file SHA-256 checksums are verified during commit and recovery
* Content-addressed reads verify integrity before returning bytes
* Merkle walks detect symlink cycles and apply an explicit symlink policy
* GC planning never performs destructive deletion

### Non-goals

* Network-distributed consensus or cloud object stores
* Encryption-at-rest (see FunctionFoundry.Security)
* Automatic orphan reclamation without an explicit host action
* POSIX advisory locking across all platforms

## Algorithm references

* SHA-256 (FIPS 180-4)
* Rabin fingerprinting for content-defined chunking (rolling polynomial hash)
* Merkle tree composition over sorted child hashes
