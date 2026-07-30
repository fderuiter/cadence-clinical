# ADR-114: Deprecate legacy econsent helper in packages ui

* **Status:** Accepted
* **Date:** 2026-07-30
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

The platform is moving towards a standardized Vue 3 single page application (SPA) architecture as part of ADR-052. The eConsent features (`apps/econsent`) are being fully integrated into modular Vue 3 components and the Subject Portal PWA. Having a standalone utility file at `packages/ui/econsent.js` duplicates logic and introduces maintenance overhead, as normalization and presentation parsing have been encapsulated directly within the reactive frontend stores and views. We need to deprecate and remove this legacy helper to ensure system-wide codebase cleanliness and eliminate duplicate validation pathways. This change is traced to PRD-SYS-001.

## 2. Decision Drivers & Constraints

* Maintain a single source of truth for eConsent client-side normalization logic in Vue 3/Pinia store structures rather than duplicate vanilla JS helpers.
* Align with regulatory and GxP guidelines (PRD-SYS-001) for strict validation of capture and consent payloads.
* Minimize technical debt during the active Vue 3 SPA views refactoring process.

## 3. Options Considered

1. **Option A (Selected)**: Completely deprecate and remove `packages/ui/econsent.js` and consolidate all normalization and state logic inside `apps/web/src/api/econsent.js` and Pinia stores.
2. **Option B (Alternative)**: Retain `packages/ui/econsent.js` as a compatibility shim and proxy all Vue 3 store calls through it.

## 4. Decision Outcome

Chosen option: **Option A** because it completely removes duplicate code pathways, aligns with the Vue 3 modular structure, and simplifies the codebase. All necessary normalization and mapping are handled reactively in frontend modules. This satisfies PRD-SYS-001 by streamlining data flow and audit boundaries.

## 5. Consequences & Trade-offs

* **Positive**: Reduced bundle size, cleaner dependency tree, and a single source of truth for eConsent presentation mapping.
* **Negative**: Requires clean-up of any legacy vanilla JS import references in the main workspace.

## 6. Implementation & Verification

* Removed `packages/ui/econsent.js`.
* Consolidated client-side integration and API contracts in `apps/web/src/api/econsent.js`.
* Verified by running all unit, integration, and UI tests (e.g. `tests/test_econsent_capture.py` and front-end vitest specs) successfully.
