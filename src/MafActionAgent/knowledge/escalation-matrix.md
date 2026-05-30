---
doc_id: EM-002
title: Escalation Matrix
category: policy
---

# EM-002 — Escalation Matrix

Defines who to page and when, for service incidents including payments
error-rate spikes (see RB-014).

## Escalation tiers

1. **Tier 1 — On-call engineer (Payments)**
   - First responder for any `High` alert on `payments.error_rate`.
   - Owns triage, runbook execution, and rollback initiation.

2. **Tier 2 — Payments engineering lead**
   - Engaged if rollback does not restore error rate below 5% within 10 minutes.
   - Authorizes extended mitigation (feature-flag disable, traffic shedding).

3. **Tier 3 — Incident commander**
   - Engaged for `Critical` severity (>15% error rate or outage).
   - Coordinates cross-team response and external communications.

## Paging SLAs

- High: acknowledge within 5 minutes.
- Critical: acknowledge within 2 minutes.
