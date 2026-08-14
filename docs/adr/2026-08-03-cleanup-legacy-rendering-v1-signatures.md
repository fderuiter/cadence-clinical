# ADR-254: Decommission Legacy Standalone Frontend Rendering Engine and Legacy V1 Signatures

- **Status:** Accepted
- **Date:** 2026-08-03
- **Authors:** @google-labs-jules[bot], @fderuiter
- **Deciders:** @fderuiter, @engineering_leads, @qa_lead

---

## 1. Context & Problem Statement

To streamline the codebase, improve maintainability, and ensure long-term GxP compliance, we need to completely decommission legacy V1 signature verification pathways and clean up the legacy standalone frontend rendering engine.

These legacy systems are redundant and duplicate functionality covered by our modernized V2/V3 signature pipelines and the centralized clinical UI library components. Retaining them increases complexity and creates style inconsistencies across the platform.

This cleanup relates directly to the system integrity, code quality, and maintainability requirements under **PRD-SYS-001**.

## 2. Decision Drivers & Constraints

- **Driver 1:** Eliminate redundant legacy code paths to simplify security audit trails and support verification pathways.
- **Driver 2:** Improve system layout consistency by relying on the unified pre-compiled shared UI rendering library.
- **Driver 3:** Ensure zero regression in active signature validation workflows or active schema integrations.
- **Constraint:** Must not affect modern signature verification methods or active CDISC/USDM version mappings.

## 3. Options Considered

### Option 1: Keep Legacy V1 Signatures and standalone renderers alongside modern pipelines

- **Overview:** Retain the legacy code paths for legacy compatibility indefinitely.
- **Pros:**
  - ✅ No immediate modification of legacy tests or unused legacy routers.
- **Cons:**
  - ❌ Increases technical debt and complicates codebase auditing.
  - ❌ Unused dictionary keys and duplicate structures are prone to style anomalies and lint errors.

### Option 2: Full Decommissioning of Legacy V1 Signatures and standalone frontend rendering code

- **Overview:** Safely remove unused legacy paths, resolve style/lint errors, and update the API contract validation tests.
- **Pros:**
  - ✅ Dramatically reduces complexity and technical debt.
  - ✅ Standardizes the rbac security model (e.g., in `packages/security/rbac.py`).
  - ✅ Enhances test performance and enforces strict bidirectional API contract testing.
- **Cons:**
  - ❌ Requires updating API contract whitelists and resolving existing test suite dependencies.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Choosing Option 2 aligns directly with our maintainability standards under **PRD-SYS-001** by removing redundant paths and standardizing code validation.

## 5. Consequences & Trade-offs

- **Positive Impact:** Cleaner RBAC definitions, correct API contract validation whitelisting, and robust test suite execution.
- **Negative Impact / Technical Debt:** Requires careful tracking of active API endpoints in test suites (e.g., newly added reorder and assignments endpoints).

## 6. Implementation & Verification

- **Affected Repositories / Services / Files:**
  - `packages/security/rbac.py` (Decommission redundant legacy roles mappings and align Schedule of Activities (SoA) permission mappings)
  - `apps/designer/soa_models.py` (Clean up forward type annotations)
  - `tests/test_api_contract_validation.py` (Whitelist active routes for reordering/assignments)
- **Verification Plan:**
  - Run `uv run pytest` to ensure all 2050 test cases pass cleanly with 85%+ code coverage.
  - Execute `uv run python scripts/validate_adrs.py` to verify ADR structure and requirement tracing.
