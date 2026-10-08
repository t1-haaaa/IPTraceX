# Usage

## First run

`./iptracex.sh` resolves the binary as: `./IPTraceX`, then the
`.iptracex/bin/` cache, then an official GitHub Release download
(HTTPS + SHA256 verified). No .NET SDK needed. Refresh anytime:

```bash
./iptracex.sh --update
```

## Interactive

```bash
./iptracex.sh
```

A main menu offers: analyze IP / domain, self IP, reverse DNS, provider
status, batch file, investigation report, configuration, exit.
Post-lookup actions (maps, JSON, report) are unchanged.

## One-shot modes

```bash
./iptracex.sh 8.8.8.8
./iptracex.sh --json 8.8.8.8
./iptracex.sh --map 8.8.8.8
./iptracex.sh --self
./iptracex.sh --domain example.com
./iptracex.sh --rdns 8.8.8.8
./iptracex.sh --report html 8.8.8.8
./iptracex.sh --providers
./iptracex.sh --file ips.txt
cat ips.txt | ./iptracex.sh --stdin
./iptracex.sh --help
./iptracex.sh --version
./iptracex.sh --no-color 8.8.8.8
./iptracex.sh --debug 8.8.8.8
./iptracex.sh --timeout 15 8.8.8.8
```

A real-world corpus ships with the repo:

```bash
./iptracex.sh --file tests/fixtures/ip_samples.txt
```

## JSON

`--json` prints pure machine-readable JSON on stdout (no banner, no
colors, no progress). Fields `geoip_quality` and `providers` are
additive; the original keys are frozen.

## Batch and stdin

Blank lines, `#` comments, duplicates, invalid IPs, and per-IP provider
failures are handled without stopping the batch. Geo providers are
queried sequentially for reliability; intel providers run under bounded
concurrency (`IPTRACEX_MAX_CONCURRENCY`) with failure isolation.
