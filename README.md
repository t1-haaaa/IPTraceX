# IPTraceX

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Linux](https://img.shields.io/badge/platform-linux--x64-lightgrey.svg)](https://github.com/t1-haaaa/IPTraceX)
[![CI](https://github.com/t1-haaaa/IPTraceX/actions/workflows/ci.yml/badge.svg)](https://github.com/t1-haaaa/IPTraceX/actions/workflows/ci.yml)

A modern multi-provider IP intelligence and geolocation CLI for Linux.

Three independent GeoIP sources. One honest answer — with consensus,
confidence scoring, and every disagreement shown, never hidden.

```
   ___ ____ _____                    __  __
  |_ _|  _ \_   _| __ __ _  ___  ___\ \/ /
   | | | |_) || | | '__/ _` |/ __|/ _ \  /
   | | |  __/ | | | | | (_| | (__  __/ /  \
  |___|_|    |_||_|  \__,_|\___\___/_/\_\

        MULTI-PROVIDER IP INTELLIGENCE & GEOLOCATION CLI

[::] Version 1.0.0
[+] Status: Ready

[?] Enter public IP address:
[-] 41.107.85.239

[+] GEOLOCATION
    Country      : Algeria
    Country Code : DZ
    Region       : Wilaya de Bechar
    City         : Bechar
    Latitude     : 31.616671
    Longitude    : -2.21667
    Timezone     : Africa/Algiers

[+] GEOIP QUALITY
    Providers    : 3/3
    Agreement    : 3/3
    Confidence   : HIGH
    Source       : Multi-provider consensus
```

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

IPTraceX (formerly IPGHOST) takes a **public** IPv4/IPv6 address and
returns network intelligence merged from three independent GeoIP
providers. When sources disagree, it says so — per provider, in the open.

## Features

- Multi-provider GeoIP (ipwho.is + ipapi.co + ipinfo.io)
- IPv4 / IPv6, public-only validation, private rejection
- Country / Region / City, ISP / Organization, ASN, hostname
- Provider consensus + confidence scoring + disagreement detection
- JSON output, batch mode, stdin mode, Google Maps links
- Self public-IP detection, provider-aware cache
- Security-first design, ASCII-first terminal UI, `--no-color` purity
- Linux/Kali CLI, self-contained .NET single binary

## Installation

```bash
git clone https://github.com/t1-haaaa/IPTraceX.git
cd IPTraceX
chmod +x iptracex.sh
./iptracex.sh
```

First run publishes the self-contained linux-x64 binary (needs .NET 8 SDK
once); afterwards no .NET install is required to run it. No root, no sudo.

Build from source:

```bash
dotnet restore
dotnet build -c Release
dotnet test
dotnet publish src/IPTraceX.CLI/IPTraceX.CLI.csproj -c Release \
  -r linux-x64 --self-contained true -p:PublishSingleFile=true \
  -o publish/linux-x64
```

## Quick Start

```bash
./iptracex.sh
```

```
[?] Enter public IP address:
[-] 8.8.8.8
```

Result: country, region, city, ISP/ASN, coordinates, Google Maps link,
and a quality block (`Providers 3/3 · Agreement 3/3 · Confidence HIGH`).

A real captured transcript lives in [`docs/demo.txt`](docs/demo.txt).

## Usage

```bash
./iptracex.sh 8.8.8.8
./iptracex.sh --json 8.8.8.8
./iptracex.sh --map 8.8.8.8
./iptracex.sh --self
./iptracex.sh --file ips.txt
cat ips.txt | ./iptracex.sh --stdin
./iptracex.sh --no-color 8.8.8.8
./iptracex.sh --debug 8.8.8.8
./iptracex.sh --file tests/fixtures/ip_samples.txt
```

Interactive menu after each lookup: analyze another IP, open Google Maps,
export JSON, save report, exit. See `docs/usage.md`.

## Multi-Provider Engine

```
public IP (validated once)
  +--> ipwho.is --> normalized GeoResult --\
  +--> ipapi.co --> normalized GeoResult ----+--> consensus vote
  +--> ipinfo.io -> normalized GeoResult ---/    (failures recorded)
```

Sequential queries (rate-limit friendly), per-provider cache, one failure
never fails the lookup. Select/order via `IPTRACEX_PROVIDERS`.

## Consensus

- **Country code** wins by majority (strongest signal).
- **Region/city/postal/timezone** by majority of providers that returned
  a value; missing data abstains; no data → `Unknown`. Never invented,
  never mapped from IP ranges by hand.
- **Coordinates** cluster within 100 km (haversine); outliers dropped.
- **ISP** matches ignoring case/corporate suffixes; **ASN** by digits.
- **Confidence**: 3/3 → HIGH, 2/3 → MEDIUM, 1/3 → LOW, 0 → UNKNOWN
  (single source → MEDIUM, uncorroborated).
- Disagreements render per provider under `[!] GEOIP PROVIDER DISAGREEMENT`.

## JSON Output

`--json` prints pure machine-readable JSON (no banner, no colors):

```json
{
  "ip": "8.8.8.8",
  "ip_version": 4,
  "geolocation": {
    "country": "United States",
    "country_code": "US",
    "region": "California",
    "city": "Mountain View",
    "latitude": 37.386,
    "longitude": -122.0838,
    "timezone": "America/Los_Angeles"
  },
  "network": { "isp": "Google LLC", "asn": "AS15169" },
  "google_maps_url": "https://www.google.com/maps?q=37.386,-122.0838",
  "geoip_quality": {
    "providers_queried": 3,
    "providers_successful": 3,
    "providers_agreeing": 3,
    "confidence": "high",
    "agreement_ratio": 1.0
  },
  "providers": []
}
```

## Configuration

| Variable | Default | Purpose |
|----------|---------|---------|
| `IPTRACEX_PROVIDERS` | `ipwho.is,ipapi.co,ipinfo.io` | Providers + priority |
| `IPINFO_TOKEN` | empty | Optional ipinfo.io token |
| `IPTRACEX_TIMEOUT` | `10` | Per-provider seconds (1–60) |
| `IPTRACEX_CACHE_TTL` | `3600` | `0` disables cache |
| `IPTRACEX_NO_COLOR` / `NO_COLOR` | empty | Plain output |
| `IPTRACEX_DEBUG` | empty | Verbose technical details |

Copy `.env.example` to `.env` (git-ignored). See `docs/configuration.md`.

## Security

HTTPS only, timeouts everywhere, bounded retry (never 429/401/403),
no shell execution, no secrets in code/tests/CI, validated provider JSON
and coordinates, sanitized filenames. Full policy: `SECURITY.md`,
controls: `docs/security.md`.

## Architecture

```
src/IPTraceX.Core/            dependency-free rules (models, validation,
                              consensus, maps, cache, JSON contract)
src/IPTraceX.Infrastructure/  I/O edges (HTTPS, providers, engine,
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
```

Covers IPv4/IPv6, private rejection, invalid input, normalization,
failures (timeout/429/5xx/malformed/missing), consensus, disagreement,
confidence, coordinates, JSON contract, CLI modes, no-color, batch,
dedup, cache isolation, Maps URLs, encoding edges, safe filenames.

## Contributing

See `CONTRIBUTING.md` and `CODE_OF_CONDUCT.md`.

## Disclaimer

IP geolocation is approximate. Results may represent ISP registration,
network infrastructure, VPN/proxy exit points, or provider estimates
rather than a user's physical location. Use only for lawful and
authorized purposes. Do not claim exact physical location.

## License

MIT — see `LICENSE`. IPTraceX is the C# successor of IPGHOST (Python);
see `CHANGELOG.md` for lineage.
