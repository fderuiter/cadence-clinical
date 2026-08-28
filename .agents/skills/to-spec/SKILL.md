---
name: to-spec
description: "Synthesize the current conversation or plan into a structured specification document aligned with PRD/SRS requirements and 21 CFR Part 11 traceability. Use when formalizing a plan into a spec."
license: MIT
metadata:
  author: Cadence Team
  version: "2.0"
---

# Cadence Spec Synthesis

Synthesizes the active conversation, settled decisions, and architectural requirements into a formal specification document using Antigravity artifact and inspection tools.

## Antigravity Tool Execution Matrix

| Phase | Antigravity Tool | Invocation Pattern | Objective |
| :--- | :--- | :--- | :--- |
| **Context Review** | `view_file` | Read `CONTEXT.md` and `docs/SDLC/01_Product_Requirements_Document_PRD.md` | Ground spec in existing requirements and glossary |
| **Ambiguity Settlement**| `ask_question` | `questions=[{"question": "Confirm Acceptance Criterion", ...}]` | Confirm critical regulatory bounds before writing spec |
| **Spec Emission** | `write_to_file` | Target: `<appDataDir>/brain/<conversation-id>/spec.md` with `ArtifactMetadata` | Emit formal specification document |

---

## 1. Specification Structure

Generate a comprehensive specification with the following sections:

1. **Title & Target Domain**: The clinical or platform area (`apps/designer`, `apps/execution`, `apps/gateway`, `apps/web`).
2. **GxP Requirement ID**: New or updated `PRD-SYS-xxx` requirement tags.
3. **Problem Statement & Clinical Context**: Regulatory motivation and clinical workflow impact.
4. **Data Models & CDISC Schema Alignment**: USDM graph nodes, PostgreSQL SQLModel tables, Pydantic schemas.
5. **API & Service Contracts**: FastAPI endpoint routes, request/response schemas, HMAC gateway auth requirements.
6. **21 CFR Part 11 Audit Trail & Security**: Cryptographic signatures, audit fields, RBAC persona permissions.
7. **Verification & Acceptance Criteria**: Specific unit, integration, and E2E test scenarios.

---

## 2. Output as Antigravity Artifact

Write the specification to `<appDataDir>/brain/<conversation-id>/spec.md` with `ArtifactMetadata` (`UserFacing: true`, `RequestFeedback: true`).
If long-term documentation is needed, also write to `docs/specs/<feature-name>.md`.

