# Contributing to IPTraceX

## Development setup

```bash
git clone https://github.com/t1-haaaa/IPTraceX.git
cd IPTraceX
chmod +x iptracex.sh
dotnet restore
dotnet build -c Release
dotnet test
./iptracex.sh
```

Kali / Debian notes: .NET 8 SDK only for building. The published
self-contained binary needs no .NET installed to run. No root needed.

## Coding standards

- .NET 8, nullable enabled, warnings as errors, small focused types.
- Zero unnecessary dependencies; every package must earn its place.
- Async all the way down for I/O; no `Thread.Sleep` on network paths.
- Never swallow errors into `catch {}` — map to `Core` exceptions.
- No secrets in code, tests, docs, or logs.
- Shell: `bash`, `set -euo pipefail`, ShellCheck-clean.

## Testing

```bash
dotnet test
```

Unit tests use fakes — no live network. Live checks are opt-in:

```bash
IPTRACEX_LIVE=1 dotnet test --filter "Integration"
```

## Pull requests

- One topic per PR, clear title (`feat:`, `fix:`, `docs:` …).
- Update `CHANGELOG.md` under `Unreleased`.
- Update `README.md` only for features that actually exist.
- Confirm `git status` shows no `.env`, `publish/`, reports, or secrets.

## Issue reporting

Include OS, .NET version (`dotnet --info`), exact command, expected vs
actual output. Redact IPs only if they are sensitive.
