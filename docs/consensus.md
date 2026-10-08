# Consensus

Only successful provider results vote. Missing fields abstain. Unknown
stays Unknown — no range-to-wilaya guessing, ever.

## Field rules

- **Country code** decides the winner (strongest signal, majority vote,
  ties break by provider order: ipwho.is, ipapi.co, ipinfo.io).
- **Region / city / postal / timezone**: majority among providers that
  returned a value. No majority or no data → `Unknown` / `null`.
- **Coordinates**: providers within 100 km (haversine) are averaged;
  outliers are dropped in favour of provider order. Invalid or
  out-of-range coordinates never vote and no Maps URL is built from them.
- **ISP**: names compare case-insensitively ignoring corporate suffixes
  (`"Telecom Algeria"` == `"TELECOM ALGERIA"`).
- **ASN**: compares by digits (`"AS15169"` == `"15169"`).

## Agreement and confidence

**Agreement** = providers matching the final country code + region +
city. Sparse fields (postal, region codes) never count against a
provider.

| Situation | Confidence |
|-----------|------------|
| All agree (2+) | `high` |
| Single source | `medium` (uncorroborated) |
| Ratio ≥ 0.5 | `medium` |
| Below 0.5 | `low` |
| None | `unknown` |

Per-field confidence (country/region/city/asn) uses the same ladder on
each field's own votes and is shown in the profile (`FIELD CONFIDENCE`)
and JSON (`consensus`).

## Disagreement display

Whenever more than one provider succeeded but fewer than all agree, the
report shows `[!] GEOIP PROVIDER DISAGREEMENT` with every source's
position. Nothing is hidden, nothing is invented.
