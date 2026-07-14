# FunctionFoundry.Security

## Threat model

### Assets

* Envelope plaintext and AEAD keys
* Pseudonymization HMAC keys and clear identifiers
* Secret-sharing polyomial randomness and reconstructed secrets

### Adversaries

* Network attackers who obtain ciphertext envelopes
* Operators with partial share sets below threshold
* Callers who misuse APIs (wrong AAD, truncated envelopes, unknown key ids)

### Guarantees

* AES-GCM authentication failures never return partial plaintext
* Envelope parsing enforces explicit size limits
* Secret comparisons use fixed-time equality helpers
* Rotation planning never deletes ciphertext

### Non-goals

* Formal proofs or FIPS certification claims
* Key distribution or HSM integration
* Claiming pseudonymization provides confidentiality against keyed attackers
* Automated destructive key deletion

## Algorithm references

* NIST SP 800-38D (AES-GCM)
* HMAC (FIPS 198-1) with SHA-256
* Shamir, A. (1979). How to Share a Secret. CACM.
