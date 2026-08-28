---
name: domain-modeling
description: "Build, sharpen, and maintain the Cadence clinical domain model, vocabulary, and architectural concepts in CONTEXT.md and docs/adr/. Use when introducing domain terms, resolving conceptual ambiguities, or authoring ADRs."
license: MIT
metadata:
  author: Cadence Team
  version: "2.0"
---

# Cadence Domain Modeling

Actively build and sharpen the project's domain model as you design. Maintain absolute fidelity to CDISC, GxP, and Cadence clinical architecture terminology.

---

## The Glossary Source of Truth (`CONTEXT.md`)

The root `CONTEXT.md` file defines the canonical glossary of clinical concepts, architecture patterns, and domain boundaries.

### Core Disciplines during Design Sessions

1. **Challenge against the glossary**:
   When a discussion uses a term that conflicts with `CONTEXT.md`, call it out immediately.
   *"Your glossary defines 'Re-Consent Gating' as blocking data capture until an ICF is registered, but you seem to mean retroactive revocation. Which is it?"*

2. **Sharpen fuzzy language**:
   Propose precise clinical terms. Avoid vague abstractions like "data item", "form event", or "manager". Use canonical terms: `Encounter`, `Activity`, `BiomedicalConcept`, `eCRF Submission`, `Query Discrepancy`.

3. **Stress-test with concrete clinical scenarios**:
   Probe edge cases (e.g. protocol amendment re-consent with in-flight visit submissions, adverse event emergency unblinding during database lock).

4. **Update `CONTEXT.md` inline**:
   When a new term crystallizes, add it immediately to `CONTEXT.md` under `# Clinical Domain Glossary` with an exact definition and `_Avoid_` hints where appropriate.

5. **Scaffold ADRs for High-Stakes Decisions**:
   When a decision is hard to reverse, surprising without context, and the result of a real trade-off, scaffold an ADR:
   ```bash
   uv run python scripts/create_adr.py --title "Title" --domain "core-platform" --req "PRD-SYS-xxx"
   ```
