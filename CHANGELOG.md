# Changelog

All notable changes to IPTraceX are documented here.
Format follows Keep a Changelog; versions follow SemVer.

## Unreleased

- feat: clean launch experience — terminal clear on interactive start,
  quiet official-release bootstrap (no curl progress, visible errors),
  developer branding (`[::] Developer: t1_haaa`), launcher `--debug`
  details, `IPTraceX_LAUNCHER_UI` de-duplication with the app header.

- fix: production launcher no longer publishes/executes a Linux ELF binary
  on Windows (clear unsupported-platform message instead); canonical binary
  name `IPTraceX` staged next to `iptracex.sh`; dev publish moved to
  `scripts/dev-publish-linux.sh`; `IPTRACEX_*` documented as official with
  `IPGHOST_*` as deprecated alias.
- feat: launcher auto-bootstrap from the official GitHub Release
  (HTTPS-only download, SHA256 verified, `.iptracex/bin` cache, `--update`
  refresh, x86_64 gate, Windows platform guard); launcher behavior tests
  in `tests/launcher/`; release ships `SHA256SUMS`.

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
