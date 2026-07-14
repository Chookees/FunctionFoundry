# FunctionFoundry.Text

Unicode-aware and approximate text analysis primitives.

## Capabilities

* **Unicode spoof detection** — normalization, confusable skeleton, mixed-script, invisible/control findings, explainable risks, versioned data; no malicious-intent claims
* **Secret candidate scanner** — high-entropy tokens, patterns, confidence, FP controls, streaming, line/col, redacted findings by default
* **Near-duplicate text index** — MinHash LSH, thresholds, incremental index, deterministic seeds, candidate+verify, memory/accuracy docs
* **Delimited-text dialect inferrer** — delimiter/quote/escape/newline/header likelihood; ranked candidates with confidence/evidence; no false certainty; sample/complexity limits

## Runtime dependencies

None beyond the .NET shared framework.

## Non-goals

* Claiming spoof findings imply malicious intent
* Full Unicode confusables (UCD) dataset — ships a compact built-in subset with documented version
* Guaranteed secret detection without false positives
