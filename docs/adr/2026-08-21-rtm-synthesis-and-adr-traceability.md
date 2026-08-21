# ADR-[NUMBER]: Automated RTM Synthesis and ADR Architectural Decisions Traceability

- **Status:** Accepted
- **Date:** 2026-08-21
- **Authors:** @google-labs-jules[bot]
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

The Cadence eClinical platform maintains strict regulatory traceability requirements across Software Requirements Specifications (SRS), Product Requirements Documents (PRD), and Architectural Decision Records (ADRs). While SRS and PRD requirements were tracked in the Requirements Traceability Matrix (RTM), ADR requirement references were previously unindexed in the consolidated RTM document, making it difficult to audit architectural decisions against platform compliance requirements (`PRD-SYS-001`, `PRD-SYS-002`).

## 2. Decision Drivers & Constraints

- **Driver 1:** 21 CFR Part 11 and GxP audit requirements demanding full bi-directional traceability across all architectural decision records and system requirements.
- **Driver 2:** Automated verification of requirement references in post-2026 ADRs against SRS (`Trace-*`) and PRD (`PRD-*`) requirement IDs (`PRD-SYS-001`, `PRD-SYS-002`).
- **Driver 3:** Zero manual maintenance overhead through automated matrix generation via `scripts/generate_rtm.py`.

## 3. Options Considered

### Option 1: Manual Maintenance of ADR Traceability Table

- **Overview:** Manually update `docs/SDLC/Requirements_Traceability_Matrix.md` whenever new ADRs are introduced or updated.
- **Pros:**
  - ✅ Simple implementation initially.
- **Cons:**
  - ❌ High risk of human error and architectural drift.
  - ❌ Fails continuous integration quality gates.

### Option 2: Automated RTM Synthesis Scanner in `generate_rtm.py` (Selected)

- **Overview:** Implement an automated `scan_adrs()` helper function in `scripts/generate_rtm.py` that scans `docs/adr/` files, parses title metadata and requirement tags, and synthesizes an **Architectural Decisions Traceability Table** in `docs/SDLC/Requirements_Traceability_Matrix.md` and `docs/SDLC/RTM.md`.
- **Pros:**
  - ✅ Guaranteed deterministic synthesis during CI build and GxP synchronization pipelines.
  - ✅ Automated enforcement via `scripts/validate_adrs.py` and `scripts/compliance_utility.py`.
- **Cons:**
  - ❌ Slight increase in RTM script execution time (~0.1s).

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Option 2 provides complete automated compliance traceability and prevents manual documentation drift across platform ADRs and SDLC artifacts.

## 5. Consequences & Trade-offs

- **Positive Impact:** All ADR requirement mappings (`PRD-SYS-001`, `PRD-SYS-002`) are continuously indexed into the platform's official GxP Requirements Traceability Matrix.
- **Negative Impact / Technical Debt:** Requires all future post-2026 ADRs to include valid requirement references verified by `scripts/compliance_utility.py`.
- **Mitigation Strategy:** Enforced via pre-commit and CI/CD quality gate `adr-validation`.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `scripts/generate_rtm.py`, `scripts/validate_adrs.py`, `scripts/compliance_utility.py`, `scripts/sync_gxp.py`, `docs/SDLC/RTM.md`.
- **Verification Plan:** Verified via `scripts/tests/test_rtm_generation_pq_validation.py` and `uv run cadence check --parallel`.
