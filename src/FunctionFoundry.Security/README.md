# FunctionFoundry.Security

Cryptographic orchestration built on BCL primitives — not a cipher invention kit, and not basic hashing wrappers.

## What this product does

It helps you **protect, pseudonymize, split, and re-key sensitive data** with explicit formats and safety rules.

### 1. Versioned envelope encryption (`EnvelopeEncryptor`)

Encrypts payloads with **AES-256-GCM** into a versioned binary envelope that carries:

* a key identifier
* associated authenticated data (AAD)
* nonce, auth tag, and ciphertext

Decrypt resolves the key by id through `IDataKeyResolver`. Authentication failures **never return partial plaintext**. Input size limits reject oversized or truncated envelopes.

### 2. Deterministic pseudonymization (`Pseudonymizer`)

Produces stable HMAC-based tokens with **tenant + context domain separation** and key versioning. Useful for irreversible-looking identifiers when the clear value is known at generation time.

**Important:** this is **not encryption**. Anyone with the HMAC key can regenerate or verify tokens for known inputs.

### 3. Threshold secret sharing (`SecretSharer`)

Shamir-style sharing over **GF(256)**. Split a secret into *n* shares with threshold *t*; any *t* shares reconstruct it. Includes share validation, duplicate detection, and binary serialization.

### 4. Key rotation planning (`KeyRotationPlanner`)

Inspects envelope metadata and builds a **deterministic plan** of which payloads still need re-encryption under a target key. It never silently deletes old ciphertext.

## When to use it

* Application-level envelope crypto with key rotation
* Privacy-preserving stable identifiers (pseudonyms)
* Split custody of high-value secrets
* Auditable migration when keys change

## Non-goals

* Inventing new ciphers or modes
* HSM / key-distribution services
* Claiming formal security proofs

## Install

```bash
dotnet add package FunctionFoundry.Security
```

## Runtime dependencies

None beyond the .NET shared framework.
