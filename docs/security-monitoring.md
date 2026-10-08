# Security Monitoring

Detection and observation only — no offensive actions. Wording rule:
`SUSPICIOUS_INPUT`, never "hack attempt", unless evidence proves it.

## What is logged

Repeated auth failures, access-denied, bursts, malformed API payloads,
invalid investigation IDs, path-traversal patterns (`PATH_TRAVERSAL`),
unexpected methods, rate-limit hits, provider abuse shapes, ingest
abuse, server errors — each with type, severity, timestamp, source,
endpoint, reason, status, correlation, count, response action.

## Alert rules (`AlertRules`, evaluated over a 60s window)

| Rule | Threshold | Severity |
|------|-----------|----------|
| 10 auth failures | ≥10 | ALERT |
| 100 invalid requests | ≥100 | ALERT |
| traversal patterns | ≥3 | ALERT |
| invalid methods | ≥20 | WARNING |
| remote audit offline | operational | WARNING |
| single provider timeout | operational | WARNING |

A single provider timeout is a PROVIDER WARNING, never an attack
claim. Alert states: NEW → ACKNOWLEDGED → RESOLVED (audit-logged).

## Email security in the dashboard

EMAIL_BREACH_CHECK / EMAIL_SECURITY_ALERT events carry breach count,
password-exposure status, severity, source, investigation, timestamp —
metadata only, never credential values.

## Troubleshooting

- `Remote: DISABLED` → set `IPTRACEX_AUDIT_ENDPOINT` + `IPTRACEX_AUDIT_SECRET`.
- `Remote: ERROR` → check `AUDIT_SEND_ERROR` in `--logs --errors`.
- Empty dashboard → verify ingest secret matches and events validate.
