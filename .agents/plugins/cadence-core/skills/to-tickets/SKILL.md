---
name: to-tickets
description: "Break a specification or plan into tracer-bullet development tickets mapped to Cadence domain routing queues. Use when decomposing a feature into actionable tickets."
license: MIT
metadata:
  author: Cadence Team
  version: "2.0"
---

# Cadence Ticket Decomposition

Decomposes specifications into vertical slice (tracer-bullet) tickets mapped to Cadence clinical module queues using Antigravity artifact and inspection tools.

## Antigravity Tool Execution Matrix

| Phase                   | Antigravity Tool               | Invocation Pattern                                        | Objective                                         |
| :---------------------- | :----------------------------- | :-------------------------------------------------------- | :------------------------------------------------ |
| **Dependency Analysis** | `find_by_name` / `grep_search` | Query existing domain services and routers                | Identify dependencies and blocking edges          |
| **Ticket Artifact**     | `write_to_file`                | Target: `<appDataDir>/brain/<conversation-id>/tickets.md` | Emit structured ticket backlog with domain queues |

---

## 1. Cadence Domain Queues

Assign each ticket to its owning domain queue:

- `PROTOCOL_DESIGNER`: Study authoring, USDM graph, SoA matrix, biomedical concepts (`apps/designer`).
- `DATA_CAPTURE_ECRF`: eCRF rendering, visit tracking, clinical observations, edit checks (`apps/execution`).
- `SUBJECT_MANAGEMENT_RTSM`: Screening, enrollment, randomization, IP kit management (`apps/execution`).
- `REPORTING_ANALYTICS`: SDTM/ADaM exports, dashboards, discrepancy metrics (`apps/reporting`).
- `PLATFORM_ADMIN_ACCESS`: OIDC auth, user personas, gateway routing (`apps/gateway`).
- `REGULATORY_COMPLIANCE`: 21 CFR Part 11 audit trails, electronic signatures, eConsent, RTM sync (`apps/econsent`, `scripts/`).

---

## 2. Ticket Structure

Each ticket should be a vertical tracer-bullet:

1. **Title**: `[QUEUE] Short Actionable Description`
2. **Requirement Link**: `@req:PRD-SYS-xxx`
3. **Seam Under Test**: The public interface and test slice.
4. **Implementation Steps**: Files to touch, models to create, endpoints to add.
5. **Quality Gates**: Commands to run (`dotnet cadence test --fast`, etc.).

---

## 3. Output as Antigravity Artifact

Write the ticket backlog to `<appDataDir>/brain/<conversation-id>/tickets.md` with `ArtifactMetadata` (`UserFacing: true`).
