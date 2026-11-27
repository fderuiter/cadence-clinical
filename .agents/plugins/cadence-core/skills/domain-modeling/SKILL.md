---
name: domain-modeling
description: "Build, sharpen, and maintain the Cadence clinical domain model, vocabulary, and architectural concepts in CONTEXT.md and docs/adr/. Use when introducing domain terms, resolving conceptual ambiguities, or authoring ADRs."
license: MIT
metadata:
  author: Cadence Team
  version: "2.0"
---

# Cadence Domain Modeling

Actively build, sharpen, and maintain the project's domain model, ubiquitous language, and architectural taxonomy in [`CONTEXT.md`](../../../../CONTEXT.md) and [`docs/adr/`](../../../../docs/adr/) using Antigravity inspection and editing tools.

## Antigravity Tool Execution Matrix

| Phase                   | Antigravity Tool       | Invocation Pattern                                              | Objective                                                        |
| :---------------------- | :--------------------- | :-------------------------------------------------------------- | :--------------------------------------------------------------- |
| **Terminology Audit**   | `grep_search`          | `Query="<term>"`, `SearchPath="apps/"`                          | Scan codebase for usage of conflicting or legacy terminology     |
| **Glossary Inspection** | `view_file`            | `AbsolutePath=".../CONTEXT.md"`                                 | Read current canonical definitions and domain boundaries         |
| **Live Glossary Edit**  | `replace_file_content` | Target: `CONTEXT.md`, surgical section edit                     | Add or update terms under `# Clinical Domain Glossary`           |
| **ADR Scaffolding**     | `run_command`          | `CommandLine="pnpm cadence scaffold adr --domain <domain> ..."` | Scaffold formal decision record when architectural models change |

---

## The Glossary Source of Truth (`CONTEXT.md`)

The root `CONTEXT.md` file defines the canonical glossary of clinical concepts, architecture patterns, and domain boundaries.

### Core Disciplines during Design Sessions

1. **Challenge against the glossary**:
   When a discussion uses a term that conflicts with `CONTEXT.md`, call it out immediately.
   _"Your glossary defines 'Re-Consent Gating' as blocking data capture until an ICF is registered, but you seem to mean retroactive revocation. Which is it?"_

2. **Sharpen fuzzy language**:
   Propose precise clinical terms. Avoid vague abstractions like "data item", "form event", or "manager". Use canonical terms: `Encounter`, `Activity`, `BiomedicalConcept`, `eCRF Submission`, `Query Discrepancy`.

3. **Stress-test with concrete clinical scenarios**:
   Probe edge cases (e.g. protocol amendment re-consent with in-flight visit submissions, adverse event emergency unblinding during database lock). If conceptual conflicts arise, invoke `ask_question`.

4. **Update `CONTEXT.md` inline (MANDATORY TOOL: `replace_file_content`)**:
   When a new term crystallizes, use `replace_file_content` to add it immediately to `CONTEXT.md` under `# Clinical Domain Glossary` with an exact definition and `_Avoid_` hints where appropriate.

5. **Scaffold ADRs for High-Stakes Decisions (MANDATORY TOOL: `run_command`)**:
   When a decision is hard to reverse, surprising without context, and the result of a real trade-off, scaffold an ADR:
   ```bash
   pnpm cadence scaffold adr --domain <domain> "Title"
   ```
