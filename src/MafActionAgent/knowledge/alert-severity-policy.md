---
doc_id: ASP-001
title: Alert Severity Policy
category: policy
metric: payments.error_rate
---

# ASP-001 — Alert Severity Policy

This policy defines how alert severity is assigned for service health metrics
such as `payments.error_rate`.

## Severity levels

| Severity | Condition | Response |
| -------- | --------- | -------- |
| **Critical** | Error rate sustained above 15%, or full outage | Page on-call immediately, engage incident commander |
| **High** | Error rate **sustained above 5%** | Page on-call, trigger runbook, prepare rollback |
| **Medium** | Error rate between 2% and 5% | Notify owning team, monitor closely |
| **Low** | Error rate between 1% and 2% | Log and review in next standup |

## Notes

- A z-score above 3.0 from the NeMo anomaly detector combined with an error rate
  above the 5% threshold is treated as a **High** severity event.
- The baseline healthy error rate for the payments service is approximately
  **0.4%**. Spikes from baseline to above 5% should be treated as incidents.
- When in doubt between two levels, choose the higher severity.
