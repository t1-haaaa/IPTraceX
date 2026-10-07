# Providers

IPTraceX queries independent GeoIP sources sequentially (reliability over
raw speed — no thread storms against free-tier rate limits) and merges
them with the consensus engine (`Consensus` in Core).

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
