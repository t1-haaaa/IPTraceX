# Investigations

An investigation freezes one analyzed target in time: profile, evidence,
errors and metadata under a unique ID (`IPX-YYYYMMDD-XXXXXX` for IPs,
e.g. `IPX-20261008-A7F31C`; `EMX-YYYYMMDD-XXXXXX` for emails).

```bash
./iptracex.sh --investigate 8.8.8.8
./iptracex.sh --investigation IPX-20261008-A7F31C
./iptracex.sh --list-investigations
./iptracex.sh --delete-investigation IPX-20261008-A7F31C
./iptracex.sh --file ips.txt --investigate   # batch mode saves each IP
./iptracex.sh --email user@example.com --investigate
./iptracex.sh --email-file emails.txt --investigate
./iptracex.sh --compare EMX-20261008-A7F31C EMX-20261008-B82D4E
```

Interactive menu `[08]` offers the same flows (new/open/list/compare/
report/delete).

## Storage

`IInvestigationStore` (file-backed default) stores UTF-8 JSON at
`investigations/YYYY-MM-DD/*.json` (`IPX-*` for IPs, `EMX-*` for emails)
with atomic writes (temp + move), ID-validated filenames only
(no traversal), and schema checks on read (`schema_version`, currently
`2.1`). Corrupt files raise a clear error; listing skips them.
Per-provider votes are embedded so stored investigations keep their
full evidence matrix. Email investigations embed the email JSON payload
(`email` section) instead of an IP profile.

No API keys, tokens, or credentials are ever written — verify with the
`NoSecretsInInvestigationFile` test.

## History and timeline

Every `--investigate` of the same target appends to its history.
Opening an investigation with prior history also renders the
`INTELLIGENCE TIMELINE` (tracked fields + consecutive changes).

> A change in GeoIP result does NOT prove physical movement. It may
> reflect database updates, provider changes, reassignment, BGP
> changes, or ISP data corrections.
