# FunctionFoundry.Security

Production-grade security orchestration over BCL cryptography primitives.

## Capabilities

* **Versioned envelope encryption** — AES-GCM with key identifiers, AAD, size limits, and rotation-aware decrypt
* **HKDF data-key derivation** — HKDF-SHA256 derivation of AES keys from a master key with salt/info context
* **Deterministic pseudonymization** — HMAC domain-separated tokens (not encryption)
* **Threshold secret sharing** — Shamir sharing over GF(256)
* **Key rotation planning** — deterministic plans from payload metadata without destroying data

## Runtime dependencies

None beyond the .NET shared framework.

## Threat model (summary)

See package docs and XML remarks. This library does not invent ciphers. Authentication failures never return partial plaintext. Pseudonymization is reversible only with the key and is not confidentiality-preserving encryption.
