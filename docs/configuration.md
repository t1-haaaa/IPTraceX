# Configuration

| Variable              | Default                        | Purpose                               |
| --------------------- | ------------------------------ | ------------------------------------- |
| `IPTRACEX_API_KEY`    | empty                          | Reserved (default providers need none)|
| `IPTRACEX_PROVIDERS`  | `ipwho.is,ipapi.co,ipinfo.io`  | Geo providers + priority order        |
| `IPTRACEX_INTEL`      | 6 intel providers              | Intel providers (comma list)          |
| `IPINFO_TOKEN`        | empty                          | Optional ipinfo.io token (more quota) |
| `IPTRACEX_ABUSEIPDB_KEY` | empty                       | Optional AbuseIPDB key (reputation)   |
| `IPTRACEX_TIMEOUT`    | `10`                           | Per-provider seconds, clamped 1–60    |
| `IPTRACEX_GLOBAL_TIMEOUT` | `90`                       | Whole-investigation seconds (10–600)  |
| `IPTRACEX_MAX_CONCURRENCY` | `4`                       | Intel providers in flight (1–16)      |
| `IPTRACEX_RISK_WEIGHTS` | defaults                     | e.g. `tor=35,proxy=20` (see risk doc) |
| `IPTRACEX_HIBP_API_KEY` | empty                      | Optional HIBP key (email breach metadata) |
| `IPTRACEX_GITHUB_TOKEN` | empty                      | Optional GitHub token (email footprint) |
| `IPTRACEX_EMAIL_RISK_WEIGHTS` | defaults               | e.g. `disposable=20,breach=10,suspicious=15` |
| `IPTRACEX_CACHE_TTL`  | `3600`                         | Seconds, `0` disables cache           |
| `IPTRACEX_NO_COLOR`   | empty                          | `1` disables colors                   |
| `IPTRACEX_DEBUG`      | empty                          | `1` shows tracebacks (no secrets)     |
| `NO_COLOR`            | empty                          | Standard opt-out, also respected      |

`IPTRACEX_PROVIDERS` (and all `IPTRACEX_*` names) are official.
Legacy `IPGHOST_*` names remain accepted as a deprecated backward-compatible
alias only when the new name is unset, and may be removed in a future major
release.

Copy `.env.example` to `.env` for local tweaks. `.env` is git-ignored and
never overrides real environment variables.

CLI flags: `--no-color`, `--debug`, `--timeout SECS`, `--self`.

## Input encoding

Surrounding whitespace and invisible edge characters (BOM, bidi marks,
stray terminal control bytes) are removed before validation; anything
unexpected inside the address is rejected with a clean message. The .NET
runtime reads the console as UTF-8 by default on Linux, and the entry
point additionally requests UTF-8 console encodings where the platform
allows. `LANG`/`LC_ALL` are never overridden.
