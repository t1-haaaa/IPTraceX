# Architecture

```
public IP (validated once)
    |
    +--> ipwho.is --\
    +--> ipapi.co ----+--> normalized GeoResult each --> consensus vote
    +--> ipinfo.io --/         (failures recorded, never fatal)
                     |
                     v
    final GeoResult + geoip_quality + providers[]
                     |
    CLI report / JSON / maps / batch / cache (per-provider keys)
```

## Projects

- `IPTraceX.Core` — dependency-free: models, validation, consensus,
  maps, cache, JSON contract. Every pure rule lives here and is unit
  tested without network.
- `IPTraceX.Infrastructure` — I/O at the edges: HTTPS client, provider
  adapters, multi-provider engine, self-IP detection, configuration
  loading (`Microsoft.Extensions.Configuration`), debug logging
  (`Microsoft.Extensions.Logging`).
- `IPTraceX.CLI` — terminal UI + all modes (interactive, direct, JSON,
  file, stdin, map, self). I/O injected (`TextReader`/`TextWriter`) so
  the CLI itself is unit tested.

Deep module: `Providers/` hides all HTTP + provider field names behind
`IGeoProvider.LookupAsync(ip) -> GeoResult`. Callers and tests cross
that one seam (`IGeoJsonFetcher` makes even the HTTP layer swappable).

## Key flows

- Lookup: `CliApp` → `MultiProviderEngine.AnalyzeAsync` (validate once,
  sequential provider queries with per-provider cache, consensus merge).
- JSON: `GeoJson.FromResult` — exact snake_case contract, nulls kept.
- Batch: dedupe (case-insensitive) → per-IP full report → summary.
  One bad IP never stops the batch.
