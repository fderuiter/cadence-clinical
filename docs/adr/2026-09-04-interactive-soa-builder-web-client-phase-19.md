# ADR-134: Interactive SoA Builder Web Client (Phase 19)

* **Status:** Accepted
* **Date:** 2026-09-04
* **Authors:** @jules
* **Deciders:** @jules

---

## 1. Context & Problem Statement
The Schedule of Activities (SoA) Builder requires a highly interactive web-based user interface to allow clinical designers to author and visualize study arms, epochs, encounters, and procedure associations. A standardized rendering helper and interactive state-management model is needed in the shared packages/ui bundle to facilitate correct and robust multi-arm presentation.

This decision implements requirements under PRD-SYS-001.

## 2. Decision Drivers & Constraints
- **Accessibility & GxP Compliance**: The matrix must render fully semantic, accessible HTML with correct colspans and rowspans.
- **Backwards Compatibility**: Simple 2D flat matrices used elsewhere across the clinical platform must continue to function without regressions.
- **Part 11 Alignment**: Requests and updates must align with GxP audit-trail requirements.

## 3. Options Considered
### Option 1: Integrate Interactive state management in the shared UI packages
Add `createSoaBuilderMatrix` and a backwards-compatible `createClinicalVisitMatrix` wrapper to `packages/ui/index.js`, alongside an in-memory interactive rendering state machine.

### Option 2: Keep everything isolated in the web application bundle
Do not share any rendering helpers between the ui package and other clinical sub-systems.

## 4. Decision Outcome
Chosen Option: Option 1. This ensures central quality control, standardized styling rules, and reusable accessibility structures.

## 5. Consequences & Trade-offs
- **Positive**: Clean separation of layout computation, absolute backwards-compatibility, and robust testability.
- **Negative**: Small increase in shared UI bundle footprint.

## 6. Implementation & Verification
- Implemented `createSoaBuilderMatrix` and `createClinicalVisitMatrix` in `packages/ui/index.js`.
- Verified using dedicated unit tests in `packages/ui/tests/index.test.js`.
