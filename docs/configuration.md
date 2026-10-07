# Configuration

| Variable              | Default                        | Purpose                               |
| --------------------- | ------------------------------ | ------------------------------------- |
| `IPTRACEX_API_KEY`    | empty                          | Reserved (default providers need none)|
| `IPTRACEX_PROVIDERS`  | `ipwho.is,ipapi.co,ipinfo.io`  | Providers + priority order            |
| `IPINFO_TOKEN`        | empty                          | Optional ipinfo.io token (more quota) |
| `IPTRACEX_TIMEOUT`    | `10`                           | Per-provider seconds, clamped 1–60    |
| `IPTRACEX_CACHE_TTL`  | `3600`                         | Seconds, `0` disables cache           |
| `IPTRACEX_NO_COLOR`   | empty                          | `1` disables colors                   |
| `IPTRACEX_DEBUG`      | empty                          | `1` shows tracebacks (no secrets)     |
| `NO_COLOR`            | empty                          | Standard opt-out, also respected      |

Legacy `IPGHOST_*` names are accepted as fallback when the new name is
unset (backward compatible with the former Python tool).

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
