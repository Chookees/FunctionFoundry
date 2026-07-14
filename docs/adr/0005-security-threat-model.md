# ADR-0005: Security package threat model and primitive policy

* **Status:** Accepted
* **Date:** 2026-07-14
* **Deciders:** FunctionFoundry architects

## Context

FunctionFoundry.Security must orchestrate cryptography without inventing ciphers or over-claiming guarantees.

## Decision

* Use only `System.Security.Cryptography` primitives (AES-GCM, HMACSHA256, RandomNumberGenerator).
* Zero third-party runtime dependencies.
* Document threat model and non-goals in package docs.
* Reject plaintext return on authentication failure.
* Treat pseudonymization explicitly as non-encryption.

## Consequences

* Narrower API surface and clearer misuse messaging.
* Hosts remain responsible for key custody and multi-tenant isolation policy.

## Alternatives considered

* Introduce libsodium via bindings — rejected for v1 dependency budget and AOT complexity.
* Custom cipher modes — rejected as unsafe.
