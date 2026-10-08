# Storage

`investigations/YYYY-MM-DD/IPX-*.json` via `FileInvestigationStore`:

- atomic temp+move writes (no half-written files)
- regex-validated IDs (`IPX-YYYYMMDD-XXXXXX`) — no path traversal
- controlled directory only; filenames never contain user paths
- UTF-8, schema-versioned reads (`schema_version`, currently `2.1`)
- corrupt files raise clear errors; listing skips them
- no shell commands, no secrets, no tokens, no credentials in files
- safe error messages (IDs echoed only after validation)

The investigation cache (`~/.cache/iptracex`, per-provider namespaces,
TTL) is separate from investigation storage and holds only normalized
provider payloads.
