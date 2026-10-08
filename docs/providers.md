# Providers

IPTraceX queries independent sources through a plugin framework
(`IIntelProvider` + `IGeoProvider`, central `ProviderCatalog`, bounded
concurrency orchestrator with health tracking). Any capability can grow to
dozens of adapters without structural changes.

## Capability matrix

| ID | Name | Category | IPv4 | IPv6 | Auth | Rate limits |
|----|------|----------|------|------|------|-------------|
| ipwho.is | ipwho.is | Geo | yes | yes | none | free tier; 429 respected |
| ipapi.co | ipapi.co | Geo | yes | yes | none | ~1000/day; 429 respected |
| ipinfo.io | IPinfo | Geo | yes | yes | optional `IPINFO_TOKEN` | anonymous throttled |
| ripestat | RIPEstat | ASN | yes | yes | none | fair use; tiny responses |
| doh-cloudflare | Cloudflare DoH | DNS | yes | yes | none | public resolver |
| doh-google | Google DoH | DNS | yes | yes | none | public resolver |
| system-dns | System DNS | DNS | yes | yes | none | local resolver |
| tor-exits | Tor Exits | Security | yes | yes | none | 1 list, cached 6h |
| cloud-ranges | Cloud Ranges | Cloud | yes | yes | none | official feeds, cached 24h |
| abuseipdb | AbuseIPDB | Reputation | yes | no¹ | `IPTRACEX_ABUSEIPDB_KEY` | 1000/day free |

¹ IPv6 check endpoint exists but is untested here; the adapter targets
IPv4 only and skips otherwise.

Dropped with reason: **BGPView** (`api.bgpview.io` does not resolve —
cannot verify, so not integrated; RIPEstat covers ASN/prefix needs).
Deferred: ARIN RDAP (registration depth), MaxMind (licensing).

## Consensus (how the final answer is built)

## Active providers (default order)

| # | Name      | Endpoint                        | Auth | IPv4 | IPv6 |
|---|-----------|---------------------------------|------|------|------|
| 1 | ipwho.is  | `GET https://ipwho.is/{ip}`     | none | yes  | yes  |
| 2 | ipapi.co  | `GET https://ipapi.co/{ip}/json/` | none | yes  | yes  |
| 3 | ipinfo.io | `GET https://ipinfo.io/{ip}/json` | optional token | yes | yes |

- One provider failing (timeout, 429, 5xx, malformed JSON, missing
  fields) never fails the lookup; zero successes is the only fatal case.
- HTTP 429 is respected, never retried aggressively; HTTP 5xx gets a
  short bounded retry (async delays only, no `Thread.Sleep`).
- `IPINFO_TOKEN` raises the ipinfo.io quota. Anonymous responses carry the
  country as a code only (`"US"`); the engine fills the full name from a
  corroborating provider when codes agree — nothing is invented.
- Override the set/order with `IPTRACEX_PROVIDERS`
  (e.g. `IPTRACEX_PROVIDERS=ipwho.is,ipapi.co`).

## Consensus (how the final answer is built)

- **Country code** decides the winner (strongest signal, majority vote,
  ties break by provider order above).
- **Region / city / postal / timezone**: majority vote among providers
  that actually returned a value; missing fields abstain. No majority or
  no data → `Unknown` (display) / `null` (JSON). IP ranges are never
  mapped to wilayas by hand.
- **Coordinates**: providers within 100 km (haversine) are averaged;
  outliers are dropped in favour of provider order. Invalid or
  out-of-range coordinates never vote and no Maps URL is built from them.
- **ISP**: names compare case-insensitively ignoring corporate suffixes
  (`"Telecom Algeria"` == `"TELECOM ALGERIA"`); **ASN** compares by digits
  (`"AS15169"` == `"15169"`).
- **Agreement** = providers matching the final country code + region +
  city. **Confidence**: all agree (2+) → `high`; single source → `medium`
  (uncorroborated); ratio ≥ 0.5 → `medium`; below → `low`; none →
  `unknown`. Disagreements are shown per provider, never hidden.

## ISP location vs physical location

IP geolocation is approximate and may identify the ISP's
registered/estimated location rather than the user's physical location.
It never yields a home address, a person, or real-time tracking.

## Adding Provider D

1. Create `Providers/FourthProvider.cs` implementing `IGeoProvider`
   (`Name`, `SupportedIpVersions`, `LookupAsync`) plus a static
   `Normalize(ip, JsonElement)` returning `GeoResult` with
   `Source = "<name>"`. Validate every field; never trust JSON.
2. Register it in `ProviderRegistry` (`Aliases` + `Build`).
3. Add mocked tests (normalization + failure modes). No live API in unit
   tests.
4. Document endpoint, auth, limits, and ToS notes here. No keys in source.

CLI, formatters, maps, batch engine, cache layout, and the JSON contract
stay untouched.
