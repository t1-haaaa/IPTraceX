# Security Policy

## Supported Versions

| Version | Supported          |
| ------- | ------------------ |
| 1.0.x   | :white_check_mark: |

## Reporting a Vulnerability

Open a GitHub Security Advisory or a private issue against
`t1-haaaa/IPTraceX`. Do **not** open a public issue with exploit details.

Include:

- affected version / commit
- reproduction steps (no real secrets)
- impact assessment

We aim to acknowledge within 72 hours and to ship a fix or mitigation
promptly. Please give us reasonable time before public disclosure.

## Responsible Disclosure

- No live attacks against third-party infrastructure.
- No exfiltration of personal data.
- Do not probe the upstream GeoIP providers abusively; respect their rate limits.

## Security Limitations

- IP geolocation is **approximate** — never exact physical tracking.
- IPTraceX sends the queried IP to the configured GeoIP providers over HTTPS.
  Do not query IPs you are not authorized to investigate.
- Never commit `.env`, API keys, reports, or personal data.
- The tool never needs root/sudo.
