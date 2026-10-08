# Evidence

Every important fact carries its sources. The evidence matrix lists, per
field and provider, the observed value and its status:

- `SUPPORT` — matches the consensus value
- `CONFLICT` — a different value (kept visible, never hidden)
- `MISSING` — the provider returned nothing for this field
- `ERROR` — the provider failed (message recorded)
- `STALE` / `CACHED` — served from cache with its age (see freshness)

```
Field       Provider       Value          Status
Country     ipwho.is       Algeria        SUPPORT
Country     ipapi          Algeria        SUPPORT
Region      Provider A     Bechar         SUPPORT
Region      Provider B     Tindouf        CONFLICT
```

Shown in investigation view, `--compare` output, reports and JSON
(`consensus` section carries supporting/conflicting provider lists).

# Confidence

Confidence is explained, not asserted. Each field reports level, reason,
supporting and conflicting providers:

- unanimous (2+) → `high` ("N independent providers agree")
- single source → `medium` ("uncorroborated")
- ratio ≥ 0.5 → `medium`, below → `low`, none → `unknown`

Reliability tiers (HIGH: official datasets; MEDIUM: reputable APIs;
UNKNOWN: unlisted) are displayed next to evidence and break exact vote
ties only — they never erase disagreement. See `docs/confidence.md`.
