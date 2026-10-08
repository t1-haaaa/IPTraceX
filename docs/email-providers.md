# Email Providers

Four adapters behind `IEmailProvider`, run by `EmailOrchestrator` over
the shared bounded runner (max concurrency, global timeout,
per-provider timeout, failure isolation, health tracking).

| ID | Name | Category | Auth | Endpoint |
|----|------|----------|------|----------|
| email-domain | Email Domain | DNS | none | Cloudflare + Google DoH, `rdap.org`, `open.kickbox.com` |
| gravatar | Gravatar | Avatar | none | `en.gravatar.com/{md5}.json` (404 = UNKNOWN) |
| github-footprint | GitHub Footprint | Footprint | optional `IPTRACEX_GITHUB_TOKEN` | `api.github.com/users/{handle}`, commit search (authed) |
| hibp | Have I Been Pwned | Breach | `IPTRACEX_HIBP_API_KEY` | `haveibeenpwned.com/api/v3/breachedaccount/{email}` |

## Notes

- `email-domain` queries **both** DoH resolvers for corroboration; one
  resolver failing never fails the provider. DNSSEC is `FOUND` only when
  validating resolvers assert AD, else `UNKNOWN`.
- Disposable uses the Kickbox open endpoint; unreachable means `UNKNOWN`
  (nullable, never guessed). Free-mail and mail-provider classification
  come from curated tables matched against observed MX hosts.
- `gravatar` hashes the normalized address with MD5 (the public Gravatar
  scheme) and reports `FOUND` only on a real profile payload.
- `github-footprint` has two tiers: unauthenticated handle existence
  (LOW, weak) and token-gated commit-authorship search (MEDIUM).
- `hibp` is skipped cleanly without a key (`failed: not configured`);
  404 means absent from the corpus; only safe metadata is kept.
- API keys come from the environment only and never appear in JSON,
  reports, investigations, cache, or logs.
