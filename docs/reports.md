# Investigation Reports

```bash
./iptracex.sh --report html 8.8.8.8
./iptracex.sh --report json 8.8.8.8
./iptracex.sh --report txt 8.8.8.8
```

Also from the interactive menu (`[07]`) and the post-lookup menu (`[04]`
saves the classic text report).

## Formats

- `txt` — plain-text profile: metadata, location, network, ASN, DNS,
  anonymity, risk + itemized evidence, providers, consensus note.
- `json` — the full profile JSON (same contract as `--json`).
- `html` — single dark-themed page, all values HTML-escaped, ASCII-safe
  content, no external assets, no scripts.
- `md` / `csv` — planned; currently rejected with a clear message.

## Layout

```
reports/
  2026-10-07/
    iptracex-8.8.8.8-20261007-120455.html
```

Filenames are sanitized (IPv6 colons become underscores, capped length,
traversal-checked). Reports never contain secrets: tokens are shown only
as `set`/`not set`, never by value.
