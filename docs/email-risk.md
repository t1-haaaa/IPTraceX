# Email Risk

Transparent additive engine (`EmailIntel.EvaluateRisk`), same shape as
the IP `RiskEngine`: every contribution carries indicator, severity,
source, evidence, weight, and explanation.

## Default weights (`IPTRACEX_EMAIL_RISK_WEIGHTS` overrides)

| Indicator | Default | Condition |
|-----------|---------|-----------|
| Disposable email | +20 | disposable service detected |
| Breach exposure | +10 + up to +5 scaling | 1+ breaches, no password/token data |
| Password exposure | +30 | a breach reports password data |
| Authentication data | +35 | a breach reports auth tokens/session material |
| Password hint/recovery | +20 | hints/recovery reported, passwords absent |
| Suspicious domain | +15 | newly registered (<30d) or no MX published |

No double counting: a password/token breach contributes its major
weight INSTEAD of the generic breach weight; email addresses and
usernames inside that breach are supporting evidence, not separate
events. Hint/recovery weight applies only when passwords are absent
(already covered by the password contribution otherwise).

Total capped at 100. Levels reuse `RiskEngine.LevelFor`.
No evidence at all yields `UNKNOWN` (null score), never zero-as-clean.

## Reputation (`EmailIntel.BuildReputation`)

Summarizes disposable status, breach count/names, and suspicious-domain
reasons. `UNKNOWN` disposable stays `UNKNOWN`; a missing breach source
is reported as unconfigured, not as clean.
