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

```
[?] Enter public IP address:
[-] 8.8.8.8
```

After a lookup:

```
[::] Actions

[01] Analyze another IP
[02] Open location in Google Maps
[03] Export JSON
[04] Save report
[00] Exit

[?] Select an option:
[-]
```

## One-shot modes

```bash
./iptracex.sh 8.8.8.8
./iptracex.sh --json 8.8.8.8
./iptracex.sh --map 8.8.8.8
./iptracex.sh --self
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
failures are handled without stopping the batch. A per-provider
concurrency storm is deliberately avoided: providers are queried
sequentially for reliability and rate-limit friendliness.
