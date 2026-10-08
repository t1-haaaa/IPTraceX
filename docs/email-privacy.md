# Email Privacy & Safety

Hard boundaries, enforced by design and tests:

- **Public only.** DNS/RDAP records, public avatar URLs, public web
  pages, and user-authorized breach metadata. No Gmail login, no OAuth,
  no private profile or mailbox access, no CAPTCHA/probe bypass.
- **No secrets in artifacts.** `IPTRACEX_HIBP_API_KEY` and
  `IPTRACEX_GITHUB_TOKEN` live in the environment only. Unit tests assert
  they never appear in JSON output, stored investigations, reports, or
  cache. Breach payloads keep name/domain/date/data-classes; the API
  returns no passwords and we store none.
- **No identity overreach.** Handle/avatar/domain matches alone are WEAK
  or LOW and worded as observations ("Public page contains matching
  email"), never as ownership. `UNKNOWN` is never rendered as `NO`.
- **No brute force.** No SMTP probing, no password-reset abuse, no
  credential-stuffing, no private-database scraping.
- **Injection-safe.** Email validation rejects control characters,
  newlines, and malformed values before any network or file use;
  reports neutralize newlines; filenames go through `SafeName` +
  `SafeResolve`; investigation IDs are strictly validated
  (`IPX-`/`EMX-` + date + 6 chars), so traversal IDs are rejected.
