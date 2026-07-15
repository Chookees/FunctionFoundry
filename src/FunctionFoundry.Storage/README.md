# FunctionFoundry.Storage

Reliable file and object workflows beyond ordinary `File` / `Directory` APIs.

## What this product does

It provides **transactional multi-file writes, content-addressed storage, content-defined chunking, and deterministic directory comparison**.

### 1. Transactional file-set writer (`TransactionalFileSetWriter`)

Creates or replaces **multiple files as one logical operation**:

* staging directory
* write-ahead journal
* per-file checksums
* atomic replace where the OS supports it (documented fallback otherwise)
* rollback and **crash recovery** with deterministic recovery reports
* cancellation without undocumented half-state

### 2. Content-addressed object store (`ContentAddressedStore`)

Streams blobs into a hash-addressed layout (SHA-256):

* deduplication by content hash
* integrity verification
* safe concurrent writers (temp + rename)
* orphan detection
* garbage-collection **planning** without automatic destructive deletion

### 3. Content-defined chunking (`ContentDefinedChunker`)

Rabin-style rolling-hash chunking with configurable min / target / max sizes. Streams input with bounded memory so similar content tends to share chunk boundaries.

### 4. Merkle file-tree snapshot and diff (`MerkleFileTree`)

Builds deterministic directory snapshots and diffs:

* added / removed / modified / moved detection
* content hashing and metadata policies
* symlink policy and cycle protection
* cross-platform path normalization

## When to use it

* Installers, exporters, or config deployers that must not leave partial file sets
* Local caches or artifact stores with dedup
* Backup/dedup pipelines needing stable chunk boundaries
* Sync tools that need a deterministic tree diff

## Non-goals

* Thin wrappers around `File.WriteAllText` or `FileStream`
* A full distributed object store or cloud SDK

## Install

```bash
dotnet add package FunctionFoundry.Storage
```

## Runtime dependencies

None beyond the .NET shared framework.
