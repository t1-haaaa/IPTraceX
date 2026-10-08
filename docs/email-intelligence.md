# Email Intelligence

IPTraceX 2.2 adds first-class **email OSINT** over the same
provider → evidence → confidence → risk → investigation pipeline as IPs.
Scope is strictly **public information** about a user-provided address:
domain/DNS records, public avatar and footprint pages, and authorized
breach metadata. No logins, no private mailbox or account access, no
credential handling of any kind.

## Quick start

```bash
./iptracex.sh --email user@example.com
./iptracex.sh --json --email user@example.com
./iptracex.sh --email user@example.com --investigate
./iptracex.sh --email user@example.com --report html
./iptracex.sh --email-file emails.txt
./iptracex.sh --email-file emails.txt --investigate
```

Interactive menu entry `[03] Analyze email` offers evidence, providers,
domain intelligence, report generation, and investigation saving.

## What it can discover

- Domain intelligence: MX hosts, mail-provider classification, SPF/DMARC
  presence, DNSSEC signal, registrar/RDAP metadata, free-mail flag.
- Disposable classification (`DETECTED` / `NOT DETECTED` / `UNKNOWN`).
- Public avatar (`FOUND` with URL, else `UNKNOWN` — absence proves nothing).
- Public footprint matches (GitHub handle page = WEAK/LOW only;
  token-gated commit authorship = MEDIUM confirmed-public signal).
- Breach metadata (Have I Been Pwned, key-gated): breach name, domain,
  date, data classes — never passwords, hashes, tokens, or dumps.
- Transparent risk score with per-indicator weights and explanations.

## What it cannot discover

- Who owns an address from a weak signal (username, avatar, domain alone).
- Private Gmail/Google account data, profile images, or contacts.
- Account existence from a footprint miss (`NO MATCH` ≠ account missing).
- Passwords, hashes, session tokens, mailbox contents — never collected.

## Identity rules

Correlation is conservative: `handle-exists` evidence is LOW confidence
and never presented as identity. Only independently corroborated public
signals raise confidence, and the UI words results as
"Public page contains matching email", never "belongs to Person X".
See `docs/email-privacy.md`.
