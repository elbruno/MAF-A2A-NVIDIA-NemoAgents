---
doc_id: RB-014
title: Runbook — Payments Service Error-Rate Spike
category: runbook
severity_threshold: 5%
metric: payments.error_rate
---

# RB-014 — Payments Service Error-Rate Spike

## Summary

This runbook governs the response to elevated error rates on the **payments
service** (`payments.error_rate`). It is the authoritative procedure when the
NeMo data-analysis agent detects a sustained spike in the payments error rate,
especially during or shortly after a deployment.

## Trigger condition

- Metric: `payments.error_rate`
- Condition: error rate **sustained above 5%** for more than 2 consecutive minutes.
- Common cause: a regression introduced by the **last deployment** to the
  payments cluster.

## Required action

1. **Raise a `High` severity alert.** A sustained error rate above 5% always
   warrants a High alert per the alert-severity policy (see ASP-001).
2. **Page the on-call engineer** for the Payments service.
3. **Attach remediation:** roll back the **last deployment** to the payments
   cluster. Rolling back is the fastest path to restore the previous healthy
   error rate (typically ~0.4%).
4. Open an incident channel and link this runbook (RB-014).

## Remediation detail — rollback

- Identify the last deploy via the release pipeline.
- Trigger an automated rollback to the previous known-good build.
- Confirm `payments.error_rate` returns below the 1% baseline before closing.

## Escalation

If the rollback does not return the error rate below 5% within 10 minutes,
escalate to the Payments engineering lead per the escalation matrix (EM-002).
