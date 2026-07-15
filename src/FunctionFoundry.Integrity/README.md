# FunctionFoundry.Integrity

Deterministic canonicalization and verifiable data structures.

## What this product does

It makes data **comparable and verifiable** across machines by fixing encoding rules and building cryptographic evidence structures.

### 1. Canonical JSON (`CanonicalJson`)

Converts JSON into a **documented deterministic profile**:

* sorted object properties
* exact number formatting rules
* Unicode / escape handling
* duplicate-property rejection
* streaming canonicalize where feasible

Identical logical documents produce identical bytes, which is required for hashing and signing.

### 2. Streaming hash manifest (`StreamingHashManifest`)

Hashes many files or streams into a deterministic manifest:

* SHA-256 (SHA-384 optional)
* path traversal protection
* explicit metadata inclusion policy
* verification reports that list exact mismatches

### 3. Merkle tree and inclusion proofs (`MerkleTree`)

Builds a Merkle tree with domain-separated leaf/internal nodes, odd-node handling, and inclusion proof generate/verify — so you can prove one item belongs to a set without shipping the whole set.

### 4. Tamper-evident hash chain (`HashChain`)

Append-only record chain with canonical encoding and verification that identifies the **first invalid record**. Sequence and timestamp policies are explicit.

**Warning:** a hash chain alone is not trusted timestamping or external anchoring.

## When to use it

* Signing / hashing APIs that need stable JSON bytes
* Release or dataset integrity manifests
* Light-weight audit logs that detect truncation or mutation
* Inclusion claims for large collections

## Non-goals

* Replacing a blockchain or TSA
* Generic JSON helpers unrelated to determinism

## Install

```bash
dotnet add package FunctionFoundry.Integrity
```

## Runtime dependencies

None beyond the .NET shared framework.
