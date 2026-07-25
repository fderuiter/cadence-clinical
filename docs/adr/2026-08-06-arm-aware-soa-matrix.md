# ADR-057: Arm-Aware Schedule of Activities (SoA) Matrix Component

* **Status:** Accepted
* **Date:** 2026-08-06
* **Authors:** @jules
* **Deciders:** @fderuiter, @jules

---

## 1. Context & Problem Statement
The Cadence Clinical platform allows clinical designers to author and visualize study schedules. To support complex multi-arm clinical trials (in accordance with CDISC USDM and GxP standards), a centralized system is needed to represent grouped Arm → Epoch → Visit/Encounter hierarchies.
Previously, the shared visit matrix only supported a flat 2D layout of visits and forms, lacking support for multi-arm groupings, timing windows, and conditional status indications within matrix cells.

## 2. Decision Drivers & Constraints
* **Backwards Compatibility:** Must keep existing single-arm clinical visit matrices functioning without regression.
* **CDISC USDM Grouping Compatibility:** Support hierarchical structures from the CDISC USDM standards.
* **Accessibility:** Generate clean, valid, and semantically accessible HTML tables with appropriate `rowspan` and `colspan` attributes.
* **Styling Integration:** Cells must visually represent applicability, conditional states, and timing details cleanly within the existing Cadence Design System.

## 3. Options Considered
### Option 1: Dedicated Sibling Component with Automatic Integration
* **Overview:** Build `createClinicalSoAMatrix` as a focused sibling component in `packages/ui/index.js`, and extend the original wrapper `createClinicalVisitMatrix` to dynamically detect the shape of input data and delegate to this new component when needed.
* **Pros:**
  * ✅ High modularity and 100% backwards compatibility.
  * ✅ Clean API design separating simple visit-form matrices from rich hierarchical SoA matrices.
  * ✅ Reuses existing styling conventions while adding scoped style sheets under `.clinical-soa-matrix`.
* **Cons:**
  * ❌ Small code addition inside the shared `packages/ui` library.

### Option 2: Replacing the Monolithic Table Component Completely
* **Overview:** Refactor `createClinicalVisitMatrix` to only consume the new SoA format, and manually migrate all existing components and tests across the repository.
* **Pros:**
  * ✅ Single rendering function in the shared library.
* **Cons:**
  * ❌ Extremely high risk of breaking existing features or other platform views that depend on the simpler 2D visit format.
  * ❌ Introduces unnecessary complexity to simpler mock views.

## 4. Decision Outcome
* **Chosen Option:** Option 1
* **Justification:** Option 1 ensures absolute backwards compatibility, prevents regression across other views, and isolates the new hierarchical mapping logic.

## 5. Consequences & Trade-offs
* **Positive Impact:**
  * Flawless rendering of multi-arm, multi-epoch, multi-visit clinical protocols.
  * Custom styles for `.status-applicable`, `.status-conditional`, and `.status-optional` improve UX significantly.
  * Cell details like timing windows are nested semantically inside `.cell-details` spans.
* **Negative Impact / Technical Debt:**
  * Small increase in the bundle size of the shared `packages/ui` package.

## 6. Implementation & Verification
* **Affected Repositories / Services:**
  * `packages/ui/index.js` (Component implementation)
  * `apps/web/src/style.css` (CSS styles)
  * `apps/web/src/views/MdrView.vue` (Visualizer integration)
* **Verification Plan:**
  * **Unit Tests:** Execute `pnpm test` and `pnpm --filter ui test` to verify the multi-level grouped headers, `colspan` computations, and cell mapping logic.
  * **Visual Verification:** Run a local development server and execute automated Playwright scripts to take screenshots and verify correct visual alignment.
