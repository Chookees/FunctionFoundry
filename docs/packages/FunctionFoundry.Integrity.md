# FunctionFoundry.Integrity

## Canonical JSON profile

Profile id: `FunctionFoundry.CanonicalJson/v1`

* Object keys sorted by UTF-8 byte order
* Duplicate property names rejected
* Numbers rendered in shortest decimal form; `NaN` and infinities rejected
* Control characters escaped as `\u00XX` lowercase hex
* UTF-8 output without BOM

Conformance-style vectors are exercised in unit tests (sorted keys, integer trimming, duplicate rejection).

## Streaming hash manifests

* Schema version `1` serialized via canonical JSON
* Default digest SHA-256; SHA-384 optional
* Relative paths only with `..`, absolute paths, and drive letters rejected
* Metadata policy explicitly controls `size` fields and custom metadata keys
* Verification returns missing paths, unexpected paths, hash mismatches, and size mismatches

## Merkle trees

* Leaf domain `0x00 || payload`
* Internal domain `0x01 || left || right`
* Odd final node duplicated before pairing
* Inclusion proofs verified independently of tree instance

## Hash chains

* SHA-256 over domain-separated canonical record bodies
* Sequence monotonicity enforced by default
* Timestamp policies: none, optional UTC ISO-8601, required UTC ISO-8601
* Verification reports first invalid record

### Non-goals

Hash chains are tamper-evident over append order and payloads. They are **not** trusted timestamping and do not prove wall-clock integrity against a malicious time source.

## Algorithm references

* FIPS 180-4 (SHA-256, SHA-384)
* RFC 8259 (JSON syntax baseline)
* RFC 8785 (informative comparison for canonicalization goals)
