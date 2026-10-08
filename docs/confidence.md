# Confidence

Levels: `high`, `medium`, `low`, `unknown` (lowercase in JSON, uppercase
in terminal). A level without its reason is not a result — every
confidence value ships with:

- level
- reason (human sentence, e.g. "2/3 providers agree; 1 conflicting result")
- supporting providers
- conflicting providers (with their values)

Rules (documented in code, tested in `EvidenceTests`):

| Situation | Level |
|-----------|-------|
| All agree (2+) | high |
| Single source | medium (uncorroborated) |
| Agreement ratio ≥ 0.5 | medium |
| Below 0.5 | low |
| No data / no success | unknown |

Per-field confidence (country/region/city/asn) is computed from each
field's own votes and shown in `FIELD CONFIDENCE`, reports and JSON.
Numerical agreement ratios are descriptive, not mathematically precise
probabilities.
