# Security Policy

## Supported versions

Security updates are provided for the latest released major version of FunctionFoundry packages (currently targeting the v1.0 line once released).

## Reporting a vulnerability

Please report security vulnerabilities privately. Do not open a public GitHub issue for undisclosed defects.

Include:

* Affected package and version
* Reproduction steps or proof of concept (kept minimal)
* Impact assessment (confidentiality, integrity, availability)
* Whether the issue is reproducible with default options

We will acknowledge reports as quickly as practical and coordinate a fix and disclosure timeline.

## Security expectations for this repository

* Cryptographic packages use `System.Security.Cryptography` primitives only; no invented ciphers or modes.
* Authentication failures must not expose partial plaintext.
* Sensitive buffers owned by libraries are cleared where practical.
* Constant-time comparison is used when comparing secrets.
* Documentation states threat models and non-goals honestly; formal security proofs are not claimed without demonstration.
