# FunctionFoundry.Text

## Threat model

### Assets

* Source text under analysis
* Secret candidate findings (redacted by default)
* Near-duplicate index shingles and signatures

### Adversaries

* Callers supplying extremely large inputs without limits
* Operators misinterpreting heuristic findings as proof of malice or certain secrets

### Guarantees

* Spoof findings describe observable Unicode properties without intent claims
* Secret findings are redacted by default with line/column positions
* Dialect inference reports confidence and evidence; never claims certainty
* Near-duplicate indexing uses deterministic seeds for reproducible buckets

### Non-goals

* Malware or phishing verdicts
* Full UCD confusables coverage
* Zero false positives for secret scanning

## Algorithm references

* Unicode normalization (UAX #15)
* MinHash / LSH for near-duplicate detection (Broder, 1997)
* Shannon entropy for token scoring

## Confusables data

Built-in subset version: `FunctionFoundry.Text.Confusables/v1` (compact common homoglyphs, not full UCD).
