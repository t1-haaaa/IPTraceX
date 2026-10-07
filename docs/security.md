# Security

IPTraceX is security-first by construction. The full policy lives in
`SECURITY.md`; this page documents the enforced controls.

## Enforced controls

- **HTTPS only**: any non-HTTPS URL is refused before a socket opens.
- **Timeouts everywhere**: per-provider timeout (default 10 s, clamped
  1–60 s) via `CancellationTokenSource`; browser open capped at 10 s.
- **Bounded retry**: transient failures (network blips, HTTP 5xx) retry at
  most twice with async backoff. 401/403/404/429 are never retried.
- **429 respected**: rate limits surface as "wait and try again"; batch
  mode continues with the next IP. No key rotation, no evasion.
- **No shell execution**: browser opening uses an argument list
  (`ProcessStartInfo.ArgumentList`, `UseShellExecute = false`); Maps URLs
  are built locally from validated coordinates and allow-listed by prefix.
- **No secrets**: no keys in source/tests/docs/CI; optional tokens come
  from env only, are never logged (debug logs carry no credentials), and
  cache refuses entries containing secret-looking content.
- **Untrusted input**: IPs validated before any network call; provider
  JSON type-checked field by field; coordinates range-checked; output
  filenames sanitized with traversal checks.
- **No `eval`/dynamic code**, no `Thread.Sleep` on network paths
  (async delays only), structured exceptions with fixed exit codes.

## Reporting

See `SECURITY.md` for supported versions and private disclosure.
