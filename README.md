# IPTraceX

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Linux](https://img.shields.io/badge/platform-linux--x64-lightgrey.svg)](https://github.com/t1-haaaa/IPTraceX)
[![CI](https://github.com/t1-haaaa/IPTraceX/actions/workflows/ci.yml/badge.svg)](https://github.com/t1-haaaa/IPTraceX/actions/workflows/ci.yml)

A modern multi-provider IP intelligence and geolocation CLI for Linux.

Enter an IP → receive a complete Intelligence Profile: location, network
and ASN intelligence, DNS, VPN/proxy/Tor verdicts, an evidence-driven
risk score, and a professional investigation report — every fact
source-attributed, every disagreement shown.

```
   ___ ____ _____                    __  __
  |_ _|  _ \_   _| __ __ _  ___  ___\ \/ /
   | | | |_) || | | '__/ _` |/ __|/ _ \  /
   | | |  __/ | | | | | (_| | (__  __/ /  \
  |___|_|    |_||_|  \__,_|\___\___/_/\_\

        MULTI-PROVIDER IP INTELLIGENCE & GEOLOCATION CLI

[::] Version 2.1.0
[::] Developer: t1_haaa
[+] Status: Ready

[?] Enter public IP address:
[-] 41.107.85.239

[+] GEOLOCATION
    Country      : Algeria
    Country Code : DZ
    Region       : Wilaya de Bechar
    City         : Bechar

[+] RISK ASSESSMENT
    Score        : unknown
    Level        : UNKNOWN
```

Maintained by **t1_haaa**.

> [!IMPORTANT]
> IP geolocation is **approximate**. Results may represent ISP
> registration, network infrastructure, VPN/proxy exit points, or provider
> estimates rather than a user's physical location. Use only for lawful and
> authorized purposes. Never a home address, a person, or GPS tracking.

## Table of Contents

- [About](#about)
- [Features](#features)
- [Installation](#installation)
- [Quick Start](#quick-start)
- [Usage](#usage)
- [Investigations](#investigations)
- [Evidence & Confidence](#evidence--confidence)
- [Multi-Provider Engine](#multi-provider-engine)
- [Consensus](#consensus)
- [JSON Output](#json-output)
- [Configuration](#configuration)
- [Security](#security)
- [Architecture](#architecture)
- [Development](#development)
- [Testing](#testing)
- [Contributing](#contributing)
- [Disclaimer](#disclaimer)
- [License](#license)

## About

IPTraceX (formerly IPGHOST) builds a complete, source-attributed
Intelligence Profile for any **public** IPv4/IPv6 address: where providers
agree, how confident each field is, what the network looks like (ASN,
prefix, registry), what DNS says, whether Tor/hosting/proxy signals fire,
and what it all means as a transparent risk score.

## Features

- Multi-provider GeoIP (ipwho.is + ipapi.co + ipinfo.io)
- ASN/BGP/registry intelligence (RIPEstat: prefix, holder, whois)
- Reverse DNS from two independent DoH resolvers + system resolver
- Domain → IP infrastructure analysis (A/AAAA, deduplicated)
- Tor exit detection (official Tor Project list)
- Hosting/cloud matching (official AWS/GCP/Cloudflare ranges)
- Optional AbuseIPDB reputation (key-gated, skipped without key)
- Evidence-driven risk score (configurable weights, UNKNOWN when dry)
- Consensus + per-field confidence + visible disagreement
- Investigation reports: txt, json, html
- Investigations: save, reopen, compare, timeline, evidence matrix
- Interactive main menu + full direct CLI
- JSON output (backward compatible, additive only)
- Batch/stdin, provider-aware cache, Google Maps, self-IP detection
- Security-first, ASCII-first terminal UI, `--no-color` purity
- Linux/Kali CLI, self-contained .NET single binary

## Installation

Kali/Linux (from source checkout — the release layout already ships the
`IPTraceX` binary next to the launcher):

```bash
git clone https://github.com/t1-haaaa/IPTraceX.git
cd IPTraceX
chmod +x iptracex.sh
./iptracex.sh
```

No .NET install needed to run. No root, no sudo.

From the GitHub release instead:

```bash
mkdir IPTraceX && cd IPTraceX
# download iptracex-linux-x64.tar.gz from the latest release, then:
tar -xzf iptracex-linux-x64.tar.gz
chmod +x iptracex.sh
./iptracex.sh
```

> [!NOTE]
> The Linux release binary (`IPTraceX`, linux-x64 ELF) cannot be executed
> directly from Windows. On Windows Git Bash, `./iptracex.sh` prints a
> clear unsupported-platform message instead of failing obscurely. Windows
> development/testing uses `dotnet run --project src/IPTraceX.CLI`.

On first run without a local binary, `./iptracex.sh` clears the screen,
shows the IPTraceX header, then quietly downloads the official Linux
release, verifies its SHA256 checksum, caches it under `.iptracex/bin/`,
and executes it. Later runs reuse the cache — no network needed.

Build from source (developers):

```bash
dotnet restore
dotnet build -c Release
dotnet test
dotnet publish src/IPTraceX.CLI/IPTraceX.CLI.csproj -c Release \
  -r linux-x64 --self-contained true -p:PublishSingleFile=true \
  -o publish/linux-x64
```

Or via helper: `./scripts/dev-publish-linux.sh`.

## Quick Start

```bash
./iptracex.sh
```

```
[01] Analyze IP
[02] Analyze domain
[03] Self IP intelligence
...
[?] Enter public IP address:
[-] 8.8.8.8
```

Result: location, ASN/prefix, DNS, anonymity verdicts, risk score,
Google Maps link, and per-field confidence — all source-attributed.

A real captured transcript lives in [`docs/demo.txt`](docs/demo.txt).

## Usage

```bash
./iptracex.sh 8.8.8.8
./iptracex.sh --json 8.8.8.8
./iptracex.sh --map 8.8.8.8
./iptracex.sh --self
./iptracex.sh --domain example.com
./iptracex.sh --rdns 8.8.8.8
./iptracex.sh --report html 8.8.8.8
./iptracex.sh --providers
./iptracex.sh --investigate 8.8.8.8
./iptracex.sh --list-investigations
./iptracex.sh --compare IPX-20261001-ABC123 IPX-20261008-DEF456
./iptracex.sh --compare-ip 1.1.1.1 8.8.8.8
./iptracex.sh --file ips.txt
cat ips.txt | ./iptracex.sh --stdin
./iptracex.sh --no-color 8.8.8.8
./iptracex.sh --debug 8.8.8.8
./iptracex.sh --file tests/fixtures/ip_samples.txt
```

Interactive menu after startup: analyze IP/domain, self IP, reverse DNS,
provider status, batch file, investigation report, investigations,
configuration, exit — plus the classic post-lookup actions.
See `docs/usage.md`.

## Investigations

```bash
./iptracex.sh --investigate 8.8.8.8
./iptracex.sh --investigation IPX-20261008-A7F31C
./iptracex.sh --list-investigations
./iptracex.sh --delete-investigation IPX-20261008-A7F31C
./iptracex.sh --batch ips.txt --investigate
```

Every analysis can be frozen under an `IPX-YYYYMMDD-XXXXXX` ID with its
profile, evidence, errors and metadata (`investigations/`), then
reopened, compared (`--compare`, `--compare-ip`), tracked over time
(timeline with neutral `CHANGE DETECTED` wording), and reported.
See `docs/investigations.md`.

## Evidence & Confidence

Every fact shows its providers: an evidence matrix (SUPPORT / CONFLICT /
MISSING / ERROR / CACHED) plus explained per-field confidence (level,
reason, supporters, conflicts). Reliability tiers are displayed and only
break exact vote ties — disagreement is never erased.
See `docs/evidence.md` and `docs/confidence.md`.

## Multi-Provider Engine

```
public IP (validated once)
  +--> ipwho.is ──────────────┐
  +--> ipapi.co ──────────────┼--> geo consensus
  +--> ipinfo.io ─────────────┘
  +--> RIPEstat ── ASN/prefix/registry ──┐
  +--> Cloudflare DoH ──┐                ├--> intel layer
  +--> Google DoH ──────┼--> PTR ────────┤   (bounded concurrency,
  +--> System DNS ──────┘                │    failure isolation)
  +--> Tor exits ── list match ──────────┤
  +--> Cloud ranges ── official feeds ───┤
  +--> AbuseIPDB (optional key) ─────────┘
```

Geo providers run sequentially (rate-limit friendly); intel providers run
with bounded concurrency (`IPTRACEX_MAX_CONCURRENCY`, default 4) under one
global timeout. One failure never stops the investigation. Select/order
via `IPTRACEX_PROVIDERS` and `IPTRACEX_INTEL`.

## Consensus

- **Country code** wins by majority (strongest signal).
- **Region/city/postal/timezone** by majority of providers that returned
  a value; missing data abstains; no data → `Unknown`. Never invented,
  never mapped from IP ranges by hand.
- **Coordinates** cluster within 100 km (haversine); outliers dropped.
- **ISP** matches ignoring case/corporate suffixes; **ASN** by digits.
- **Confidence**: unanimous (2+) → HIGH; single source → MEDIUM;
  ratio ≥ 0.5 → MEDIUM; below → LOW; none → UNKNOWN. Per-field confidence
  (country/region/city/asn) is computed and shown.
- Disagreements render per provider under `[!] GEOIP PROVIDER DISAGREEMENT`.

See `docs/consensus.md`.

## Risk Score

Evidence-driven and fully itemized — every point shows its indicator,
severity, source, evidence, weight and explanation:

```
[+] RISK ASSESSMENT
    Score        : 35/100
    Level        : LOW
    +35 Tor exit node [HIGH] (tor-exits)
```

Defaults: tor 35, proxy 20, hosting 15, vpn 15, abuse ≤25 (scaled).
Levels: 0–19 VERY LOW, 20–39 LOW, 40–59 MEDIUM, 60–79 HIGH, 80–100
CRITICAL. No evidence → UNKNOWN (never manufactured certainty).
Weights tunable via `IPTRACEX_RISK_WEIGHTS`. See `docs/risk-engine.md`.

Tor = official exit list (HIGH both ways). Hosting = official cloud
ranges only. VPN/proxy = `UNKNOWN` unless the optional AbuseIPDB key is
configured — cloud ownership alone is never treated as VPN evidence.

## ASN Intelligence

Prefix + covering ASNs (RIPEstat network-info), holder and registry data
(RIPEstat whois: NetName, Organization, Country), merged with consensus
ASN/ISP. Displayed with per-fact sources.

## Domain Analysis

```bash
./iptracex.sh --domain example.com
```

Resolves A/AAAA via the system resolver (DNS only — no port scanning,
no active probing), drops non-public hits, and builds a full profile per
discovered IP plus an infrastructure summary.

## Reverse DNS

```bash
./iptracex.sh --rdns 8.8.8.8
```

PTR from Cloudflare DoH + Google DoH + system resolver. Corroborated
(2+) → HIGH; single → MEDIUM; conflicting → LOW; none observed →
`none observed`.

## Reports

```bash
./iptracex.sh --report html 8.8.8.8
./iptracex.sh --report json 8.8.8.8
./iptracex.sh --report txt 8.8.8.8
```

Saved under `reports/YYYY-MM-DD/` with safe filenames: metadata, target,
location, network, ASN, DNS, anonymity, risk + evidence, providers,
consensus/confidence, errors, timestamp, tool version. See
`docs/reports.md`.

## JSON Output

`--json` prints pure machine-readable JSON (no banner, no colors).
Original keys (`ip`, `ip_version`, `geolocation`, `network`,
`google_maps_url`, `geoip_quality`, `providers`) are frozen; new
sections (`target`, `dns`, `security`, `risk`, `asn`, `consensus`,
`metadata`) are additive.

## Configuration

| Variable | Default | Purpose |
|----------|---------|---------|
| `IPTRACEX_PROVIDERS` | `ipwho.is,ipapi.co,ipinfo.io` | Geo providers + priority |
| `IPTRACEX_INTEL` | `ripestat,doh-cloudflare,doh-google,system-dns,tor-exits,cloud-ranges` | Intel providers |
| `IPINFO_TOKEN` | empty | Optional ipinfo.io token |
| `IPTRACEX_ABUSEIPDB_KEY` | empty | Optional AbuseIPDB key (enables reputation) |
| `IPTRACEX_TIMEOUT` | `10` | Per-provider seconds (1–60) |
| `IPTRACEX_GLOBAL_TIMEOUT` | `90` | Whole-investigation seconds (10–600) |
| `IPTRACEX_MAX_CONCURRENCY` | `4` | Intel providers in flight (1–16) |
| `IPTRACEX_RISK_WEIGHTS` | defaults | e.g. `tor=35,proxy=20` |
| `IPTRACEX_CACHE_TTL` | `3600` | Seconds, `0` disables cache |
| `IPTRACEX_NO_COLOR` / `NO_COLOR` | empty | Plain output |
| `IPTRACEX_DEBUG` | empty | Verbose technical details |

Legacy `IPGHOST_*` names remain as deprecated aliases. Copy
`.env.example` to `.env` (git-ignored). See `docs/configuration.md`.

## Security

HTTPS only, timeouts everywhere, bounded retry (never 429/401/403),
no shell execution, no secrets in code/tests/CI, validated provider JSON
and coordinates, sanitized filenames, traversal-checked output paths.
Full policy: `SECURITY.md`, controls: `docs/security.md`.

## Architecture

```
src/IPTraceX.Core/            dependency-free rules (models, validation,
                              consensus, risk, maps, cache, JSON contract)
src/IPTraceX.Infrastructure/  I/O edges (HTTPS, 10 providers, geo engine,
                              intel orchestrator, profiler, reports,
                              config, logging, self-IP)
src/IPTraceX.CLI/             terminal UI + every mode
tests/IPTraceX.Core.Tests/    xUnit, fully mocked
tests/IPTraceX.Integration.Tests/  live, opt-in (IPTRACEX_LIVE=1)
```

See `docs/architecture.md`.

## Development

```bash
dotnet restore
dotnet build -c Release
dotnet test
```

Nullable enabled, warnings as errors, async I/O, structured exceptions,
single shared `HttpClient`, no `Thread.Sleep` on network paths.

## Testing

```bash
dotnet test                                   # unit (mocked, offline)
IPTRACEX_LIVE=1 dotnet test --filter Integration  # live providers
./tests/launcher/run_tests.sh                 # launcher behavior
```

Covers IPv4/IPv6, private/loopback/multicast/reserved rejection, invalid
input, normalization ×7 providers, failures (timeout/429/5xx/malformed/
missing), consensus, disagreement, confidence, coordinates, risk levels,
Tor/DoH/cloud parsing, ASN parsing, DNS parsing, domain resolution,
JSON contract + compatibility, CLI modes/flags, menu, no-color, batch,
dedup, cache isolation + provider isolation, Maps URLs, encoding edges,
safe filenames, reports, provider registry.

## Contributing

See `CONTRIBUTING.md` and `CODE_OF_CONDUCT.md`.

## Disclaimer

IP geolocation is approximate. Results may represent ISP registration,
network infrastructure, VPN/proxy exit points, or provider estimates
rather than a user's physical location. Risk scores are heuristic
summaries of observed evidence, not verdicts. Use only for lawful and
authorized purposes (network administration, troubleshooting, security
research, education). Do not claim exact physical location.

## License

MIT — see `LICENSE`. IPTraceX is the C# successor of IPGHOST (Python);
see `CHANGELOG.md` for lineage.
