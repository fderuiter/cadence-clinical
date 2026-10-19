---
name: triage
description: "Categorize, route, and prioritize clinical issues, bug reports, and development tickets across Cadence functional domain queues and SLAs. Use when triaging incoming issues or PRs."
license: MIT
metadata:
  author: Cadence Team
  version: "2.0"
---

# Cadence Issue & Ticket Triage

Categorizes incoming issues, bug reports, and clinical inquiries, mapping them to functional domain queues and SLA tracking models using Antigravity search, interactive questioning, and artifact generation.

## Antigravity Tool Execution Matrix

| Phase                    | Antigravity Tool               | Invocation Pattern                                             | Objective                                                 |
| :----------------------- | :----------------------------- | :------------------------------------------------------------- | :-------------------------------------------------------- |
| **Context & Dupe Check** | `grep_search` / `find_by_name` | Search issues and codebase for related tickets                 | Detect duplicate reports or known failure sites           |
| **Priority Assignment**  | `ask_question`                 | `questions=[{"question": "Assign Issue Priority", ...}]`       | Interactively confirm severity and queue routing          |
| **Triage Brief**         | `write_to_file`                | Target: `<appDataDir>/brain/<conversation-id>/triage_brief.md` | Emit structured triage brief for agent or human execution |

---

## 1. Domain Routing Queues

- `PROTOCOL_DESIGNER`: Study authoring, USDM graph, SoA matrix (`apps/designer`).
- `DATA_CAPTURE_ECRF`: eCRF data entry, visit tracking, edit checks (`apps/execution`).
- `SUBJECT_MANAGEMENT_RTSM`: Screening, enrollment, randomization, IP kits (`apps/execution`).
- `REPORTING_ANALYTICS`: SDTM/ADaM exports, dashboards (`apps/execution`).
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

### MANDATORY TOOL INVOCATION (`ask_question`)

You **MUST** call `ask_question` when confirming triage classification:

```json
{
  "questions": [
    {
      "question": "Confirm Domain Queue and Priority for Issue",
      "options": [
        "(Recommended) Route to DATA_CAPTURE_ECRF queue with High (P1) SLA",
        "Route to REGULATORY_COMPLIANCE queue with Critical (P0) SLA",
        "Mark as needs-info and request CRA reproducer steps"
      ],
      "is_multi_select": false
    }
  ]
}
```
