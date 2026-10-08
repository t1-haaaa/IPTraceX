# Architecture

```
public IP (validated once)
    |
    +--> ipwho.is --\
    +--> ipapi.co ----+--> geo consensus (sequential, rate-limit friendly)
    +--> ipinfo.io ---/              |
                                     v
    +--> ripestat -------- ASN/prefix/registry --\
    +--> doh-cloudflare --\                       |
    +--> doh-google ------+--> PTR corroboration -+--> intel layer
    +--> system-dns ------/                       |   (bounded concurrency,
    +--> tor-exits ------- exit-list match -------+    global timeout,
    +--> cloud-ranges ---- official feeds -------+    failure isolation)
    +--> abuseipdb ------- reputation (optional key)
                                     |
                                     v
                profile: consensus + confidence + ASN + DNS
                       + anonymity + risk + evidence + metadata
                                     |
            CLI report / JSON / HTML / batch / cache (per-provider keys)
```

## Projects

- `IPTraceX.Core` — dependency-free: models, validation, consensus,
  risk engine, maps, cache, JSON contract. Every pure rule lives here
  and is unit tested without network.
- `IPTraceX.Infrastructure` — I/O at the edges: HTTPS client, 10
  provider adapters, geo engine, intel orchestrator, profiler,
  reports, configuration loading (`Microsoft.Extensions.Configuration`),
  debug logging (`Microsoft.Extensions.Logging`), self-IP detection.
- `IPTraceX.CLI` — terminal UI + every mode (interactive menu, direct,
  JSON, file/stdin batch, map, self, domain, rdns, reports, providers).
  I/O injected (`TextReader`/`TextWriter`) so the CLI itself is unit
  tested.

Deep module: `Providers/` hides all HTTP + provider field names behind
`IGeoProvider` / `IIntelProvider`. Callers and tests cross those seams
(`IGeoJsonFetcher` makes even the HTTP layer swappable).

## Key flows

- Lookup: `CliApp` → `MultiProviderEngine.AnalyzeAsync` (validate once,
  sequential provider queries with per-provider cache, consensus merge).
- JSON: `GeoJson.FromResult` — exact snake_case contract, nulls kept.
- Batch: dedupe (case-insensitive) → per-IP full report → summary.
  One bad IP never stops the batch.
