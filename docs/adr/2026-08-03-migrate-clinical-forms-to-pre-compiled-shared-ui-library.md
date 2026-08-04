# ADR-254: Migrate Clinical Forms to Pre-Compiled Shared UI Library

* **Status:** Accepted
* **Date:** 2026-08-03
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Historically, the core clinical form engine and input fields were implemented directly inside the primary clinical execution dashboard (`apps/web/src/components/clinical`). As the Cadence Clinical platform expands to support additional consumer applications and patient-facing portals (such as `apps/subject-portal` and offline single-page apps), duplicating form-rendering logic across app boundaries introduces maintenance overhead, risks validation drift, and violates standard design system consistency.

Furthermore, a previous architectural decision (ADR-251) rejected migrating shared primitives to standard SPA frameworks like Vue due to concerns over bundler compatibility, raw source imports, and local compiler configuration drift. To resolve these challenges and enable standardized, accessible clinical interfaces across different applications, we need a robust approach to migrate clinical forms into a reusable, pre-compiled shared UI library (`packages/ui`) with full Vue support and modern tooling.

This decision supports compliance tracing and standardized trial audits under **PRD-SYS-001** and accessibility requirements under **PRD-CRF-015**.

## 2. Decision Drivers & Constraints

* **Preventing Compiler Drift:** Ensure that consumer applications import production-ready, standardized assets rather than raw `.vue` files, which avoids local bundler configuration differences.
* **Decoupled Architecture:** Remove tightly coupled runtime store dependencies (such as direct imports of the main app's authentication store inside `ClinicalQueryPanel.vue`) to make components truly independent.
* **Accessibility and UX Consistency (WCAG):** Guarantee that all clinical portals maintain a unified level of WCAG compliance and design cohesion by centralizing accessible composables (`useFocusTrap` and `useEscapeClose`) and structural primitives.
* **Maintainability & Scope:** Keep the migration scoped by excluding complex layout grids or regulatory document views, which should remain application-specific.

## 3. Options Considered

### Option A: Standard pre-compiled shared UI library with Vite bundling (Selected)
* **Overview:** Configure `packages/ui` to compile into distributable ESM and CJS formats using a dedicated Vite bundling pipeline. Move the clinical form orchestration engine and all leaf inputs (such as `ClinicalFormField.vue`, `ClinicalInput.vue`, `ClinicalRadioGroup.vue`, `ClinicalLookupInput.vue`, `ClinicalQueryFlag.vue`, and `ClinicalQueryPanel.vue`) along with their custom accessibility composables into `packages/ui`.
* **Pros:**
  * ✅ Solves compiler drift by compiling `.vue` and JS assets into stable distributed assets (`packages/ui/dist/[index.js]`).
  * ✅ Promotes absolute parity in rendering behavior, styles, and accessibility across portals.
  * ✅ Decouples internal logic dynamically via runtime store lookup using Pinia instead of static relative imports.
* **Cons:**
  * ❌ Increases upfront build step overhead inside the workspace (`packages/ui` must be built).

### Option B: Raw source sharing with workspace aliases
* **Overview:** Share raw `.vue` source components directly via workspace aliases or symbolic links without pre-compiling.
* **Pros:**
  * ✅ Simpler package setup with no intermediate build steps.
* **Cons:**
  * ❌ Consumer applications must have perfectly identical webpack/vite configurations to compile raw `.vue` files, risking compilation drift.
  * ❌ Hard to share and integrate cleanly into environments that do not use standard Vue build toolchains.

## 4. Decision Outcome

Chosen option: **Option A (Standard pre-compiled shared UI library with Vite bundling)** because it satisfies our strict accessibility constraints (**PRD-CRF-015**) and ensures secure, robust, and consistent clinical form-rendering behavior across the single-page Single Page Applications. By building standard ESM and CJS distributions, we guarantee future-proof imports with zero runtime compiler friction.

## 5. Consequences & Trade-offs

* **Positive Impact:** Elimination of duplicate clinical input and query code; high parity across patient and investigator portals.
* **Positive Impact:** Guaranteed WCAG accessibility compliant out-of-the-box since keyboard-traps and escape close flows are bundled with the primitives.
* **Negative Impact / Technical Debt:** Introduces a requirement that `packages/ui` must be compiled (`pnpm --filter ui build`) before consuming applications are run or tested.
* **Mitigation Strategy:** Configured monorepo build scripts and validation pipelines to build dependencies sequentially.

## 6. Implementation & Verification

* **Shared UI Package (`packages/ui`):**
  * Added `vite.config.js` with multi-format (ESM/CJS) library build configuration.
  * Migrated clinical components under `src/components/clinical/`.
  * Migrated accessibility composables: `packages/ui/src/composables/useFocusTrap.js` and `packages/ui/src/composables/useEscapeClose.js`.
  * Updated `index.js` to export these components and composables.
* **Consumer Application (`apps/web`):**
  * Removed legacy duplicated component implementations in `src/components/clinical/`.
  * Configured `apps/web/vite.config.js` to map `ui` to the pre-compiled `packages/ui/dist/[index.js]`.
  * Updated views (such as `EcrfView.vue`) and unit tests to import components directly from `ui`.
* **Verification Plan:**
  * Verified using standard linter/formatter scripts (`pnpm lint`, `pnpm format`).
  * Executed 255 frontend unit and accessibility tests under `apps/web` via `pnpm --filter web test` with 100% test parity.
