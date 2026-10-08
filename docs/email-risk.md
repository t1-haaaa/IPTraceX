# Email Risk

Transparent additive engine (`EmailIntel.EvaluateRisk`), same shape as
the IP `RiskEngine`: every contribution carries indicator, severity,
source, evidence, weight, and explanation.

## Default weights (`IPTRACEX_EMAIL_RISK_WEIGHTS` overrides)

| Indicator | Default | Condition |
|-----------|---------|-----------|
| Disposable email | +20 | disposable service detected |
| Breach exposure | +10 + up to +5 scaling | 1+ breaches in corpus (+5 per extra breach, capped) |
| Suspicious domain | +15 | newly registered (<30d) or no MX published |

Total capped at 100. Levels reuse `RiskEngine.LevelFor`.
No evidence at all yields `UNKNOWN` (null score), never zero-as-clean.

## Reputation (`EmailIntel.BuildReputation`)

Summarizes disposable status, breach count/names, and suspicious-domain
reasons. `UNKNOWN` disposable stays `UNKNOWN`; a missing breach source
is reported as unconfigured, not as clean.
