# Changelog

All notable changes to IPTraceX are documented here.
Format follows Keep a Changelog; versions follow SemVer.

## Unreleased

## 1.0.0 — 2026-10-07

- Complete migration from Python to C# / .NET 8, renamed IPGHOST → IPTraceX.
  The Python lineage (IPGHOST 0.1.x) remains visible in git history.
- Same CLI contract: interactive / direct / `--json` / `--file` /
  `--stdin` / `--map` / `--self` / `--help` / `--version` / `--no-color` /
  `--debug` / `--timeout`, same exit codes, same JSON keys (plus additive
  `geoip_quality` + `providers`).
- Multi-provider consensus engine (ipwho.is + ipapi.co + ipinfo.io),
  confidence scoring, disagreement display, provider-aware cache.
- xUnit suite (unit, mocked; integration opt-in), GitHub Actions CI,
  self-contained linux-x64 single-file publish via `./iptracex.sh`.
