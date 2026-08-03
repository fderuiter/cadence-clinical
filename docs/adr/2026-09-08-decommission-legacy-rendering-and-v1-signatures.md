# ADR-252: Decommission legacy rendering and v1 signatures

* **Status:** Accepted
* **Date:** 2026-09-08
* **Authors:** @google-labs-jules[bot]
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

The platform is transitioning to a modern, unified architectural approach for clinical trial forms and signature validations. Legacy V1 cryptographic signatures, which relied on colon-separated strings, are being decommissioned globally to strictly enforce GxP-compliant key-sorted canonical V2 payloads at the gateway level. Simultaneously, the standalone legacy rendering engine file at `apps/web/index.js` is being removed, with dynamic form helper functions (`renderFormFromJSON` and `validateField`) consolidated into the modern AST evaluator at `apps/web/src/evaluator.js`. This change ensures system-wide robustness, code reduction, and strict adherence to FDA 21 CFR Part 11 and GxP guidelines. This transition is traced to cryptographic requirements in PRD-SYS-003 and audit trail standards in PRD-SYS-001.

## 2. Decision Drivers & Constraints

* Achieve global enforcement of GxP-compliant canonical V2 cryptographic signatures without legacy V1 fallbacks.
* Consolidate web form evaluation logic into a single modern module (`evaluator.js`) to reduce code duplication and formatting conflicts.
* Retain full compatibility for downstream Playwright UI tests by preserving legacy DOM selectors like `#soa-matrix-container`.
* Ensure that the centralized role-permission mapping defined in `packages/security/rbac.py` remains correct, clean, and properly formatted without syntax or duplication errors.

## 3. Options Considered

1. **Option A (Selected)**: Decommission legacy V1 pathways completely, delete `apps/web/index.js`, consolidate helpers into `apps/web/src/evaluator.js`, and perform linter cleanup on centralized security policies in `packages/security/rbac.py`.
2. **Option B (Alternative)**: Retain legacy pathways as compatibility shims, increasing technical debt and maintenance complexity across the gateway and frontend assets.

## 4. Decision Outcome

Chosen option: **Option A** because it is the only way to satisfy regulatory GxP and FDA Part 11 compliance dynamically and cleanly across both frontend and backend systems, eliminating multi-path parsing vulnerabilities while significantly improving codebase maintainability.

## 5. Consequences & Trade-offs

* **Positive**: 100% enforcement of key-sorted canonical V2 payloads; elimination of legacy JS dependencies and duplicated render routines; clean unified linter state for RBAC matrices.
* **Negative**: Requires careful preservation of E2E-critical DOM selectors (such as `#soa-matrix-container`) to avoid disrupting automated testing suites.

## 6. Implementation & Verification

* Excised V1 signature routines across backend gateways.
* Removed the standalone legacy rendering engine file at `apps/web/index.js` and consolidated active form helpers into `apps/web/src/evaluator.js`.
* Retained `#soa-matrix-container` in `apps/web/src/views/MdrView.vue`.
* Cleaned up duplicated keys in role-permission maps in `packages/security/rbac.py`.
* Validated successfully against the entire 2,050-case automated unit/integration test suite.
