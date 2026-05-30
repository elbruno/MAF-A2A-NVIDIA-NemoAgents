---
doc_id: RT-007
title: Incident Report Templates
category: template
---

# RT-007 — Incident Report Templates

Standard templates for generating incident and metrics reports after a payments
error-rate event (RB-014).

## Incident summary report

- **Incident ID:** auto-generated
- **Metric:** `payments.error_rate`
- **Detected by:** NeMo Data Analysis Agent (trend + anomaly detection)
- **Peak error rate:** value and timestamp
- **Severity:** per ASP-001 (High when sustained above 5%)
- **Action taken:** alert raised, on-call paged, rollback of last deploy
- **Resolution:** error rate returned to ~0.4% baseline
- **Runbook used:** RB-014

## Metrics report

- Time window analyzed
- Trend direction and strength
- Anomalies detected (count, z-scores)
- Threshold breaches against ASP-001
- Recommended follow-up actions

## Distribution

Reports are queued for delivery to the analytics team and the Payments
engineering distribution list.
