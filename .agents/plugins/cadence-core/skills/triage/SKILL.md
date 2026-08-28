---
name: triage
description: "Categorize, route, and prioritize clinical issues, bug reports, and development tickets across Cadence functional domain queues and SLAs. Use when triaging incoming issues or PRs."
license: MIT
metadata:
  author: Cadence Team
  version: "2.0"
---

# Cadence Issue & Ticket Triage

Categorizes incoming issues, bug reports, and clinical inquiries, mapping them to functional domain queues and SLA tracking models.

---

## 1. Domain Routing Queues
- `PROTOCOL_DESIGNER`: Study authoring, USDM graph, SoA matrix (`apps/designer`).
- `DATA_CAPTURE_ECRF`: eCRF data entry, visit tracking, edit checks (`apps/execution`).
- `SUBJECT_MANAGEMENT_RTSM`: Screening, enrollment, randomization, IP kits (`apps/execution`).
- `REPORTING_ANALYTICS`: SDTM/ADaM exports, dashboards (`apps/reporting`).
- `PLATFORM_ADMIN_ACCESS`: OIDC auth, user personas, gateway (`apps/gateway`).
- `REGULATORY_COMPLIANCE`: 21 CFR Part 11 audit trails, eConsent, RTM sync (`apps/econsent`, `scripts/`).

---

## 2. Canonical Triage Roles
1. `needs-triage`: Newly filed, awaiting queue and priority assignment.
2. `needs-info`: Requires additional reproducing steps or clinical clarification.
3. `ready-for-agent`: Fully specified vertical slice ready for AI agent execution.
4. `ready-for-human`: Requires human clinical data manager or CRA review.
5. `wontfix`: Out of scope or invalid.

---

## 3. SLA & Priority Assessment
- **Critical (P0)**: Patient safety impact, audit trail failure, or data loss risk. Initial response < 15 min.
- **High (P1)**: Protocol amendment block, unhandled runtime exception, or CI gate failure. Initial response < 1 hr.
- **Normal (P2)**: Standard feature enhancement, refactoring, or minor UI fix. Initial response < 24 hr.
