# FunctionFoundry.Text

Complex text analysis for spoof detection, secret hunting, near-duplicates, and CSV/TSV dialect discovery.

## What this product does

It analyzes text for **risk signals and similarity**, with explainable results and hard processing limits.

### 1. Unicode spoof / confusable detection (`UnicodeSpoofDetector`)

Finds lookalike and mixed-script abuse patterns:

* Unicode normalization checks
* confusable skeleton generation (versioned compact dataset)
* mixed-script detection
* invisible / control-character findings
* explainable risk findings

Does **not** claim that a heuristic result proves malicious intent.

### 2. Secret-candidate scanner (`SecretCandidateScanner`)

Scans large text for likely secrets:

* high-entropy tokens
* configurable patterns
* context-aware confidence scores and false-positive controls
* streaming support with line/column reporting
* **redacted findings by default**

### 3. Near-duplicate text index (`NearDuplicateTextIndex`)

Indexes documents with MinHash / LSH-style techniques:

* configurable similarity thresholds
* incremental indexing
* deterministic seeds
* candidate retrieval plus exact similarity verification
* documented memory / accuracy trade-offs

### 4. Delimited-text dialect inference (`DelimitedTextDialectInferrer`)

Infers delimiter, quote, escape, newline, and header likelihood from samples. Returns **ranked candidates** with confidence and evidence — never silently claims certainty. Sample size and complexity are limited.

## When to use it

* Homograph / phishing username checks
* Pre-commit or CI secret scanning of text blobs
* Deduplicating support tickets, docs, or logs
* Auto-detecting messy CSV dialects before parsing

## Non-goals

* Full OCR or NLP pipelines
* Guaranteeing zero secret false positives

## Install

```bash
dotnet add package FunctionFoundry.Text
```

## Runtime dependencies

None beyond the .NET shared framework.
