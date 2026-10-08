# Risk Engine

The risk score is a heuristic summary of observed evidence — not a
verdict, never GPS, never identity.

## Levels

| Score | Level      |
|-------|------------|
| —     | UNKNOWN (no evidence) |
| 0–19  | VERY LOW   |
| 20–39 | LOW        |
| 40–59 | MEDIUM     |
| 60–79 | HIGH       |
| 80–100| CRITICAL   |

## Default weights

| Indicator | Weight | Severity when firing |
|-----------|--------|----------------------|
| Tor exit node | 35 | HIGH |
| Proxy | 20 | MEDIUM |
| Hosting/datacenter | 15 | LOW |
| VPN | 15 | MEDIUM |
| Abuse reports | 0–25 scaled (`score/4`) | MEDIUM (HIGH ≥ 75) |

Total is capped at 100. Override with
`IPTRACEX_RISK_WEIGHTS="tor=35,proxy=20,hosting=15,vpn=15,abuse=25"`
(unknown keys ignored, out-of-range values rejected).

## Evidence rules (honesty first)

- **Tor**: fires only on the official Tor Project bulk exit list
  (HIGH either way; absence is meaningful because the list is complete).
- **Hosting**: fires only inside official AWS/GCP/Cloudflare ranges with
  the matched prefix cited. Absence is LOW-confidence (other hosts exist).
- **VPN/proxy**: fire only from a reputation source's explicit usage
  classification (AbuseIPDB, optional key). Without such a source the
  verdict is UNKNOWN with the reason stated. Cloud ownership alone is
  never treated as VPN evidence.
- **Abuse**: fires only from AbuseIPDB `abuseConfidenceScore > 0`
  (optional key). Without the key there is simply no such evidence.

Every fired indicator records indicator, severity, source, evidence,
weight and a human explanation, rendered in the report and JSON.
