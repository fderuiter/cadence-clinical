# ADR-120: Shared UI Workspace Resolution and Compliance Enhancements

* **Status:** Accepted
* **Date:** 2026-08-30
* **Authors:** @jules
* **Deciders:** @reviewer1, @reviewer2

---

## 1. Context & Problem Statement
The Cadence Clinical Platform requires a robust, standardised shared UI compilation and distribution pipeline to serve clinical workspaces and patient portals. Previously, the monorepo relied on direct Vite aliases to map `@cadence/ui` imports directly to raw JS source files, which bypassed standard workspace boundaries and generated dependency resolution issues. Furthermore, 21 CFR Part 11 electronic signature workflows and offline data synchronisation loops were duplicated and inconsistently implemented across the clinical coordinator app (`apps/web`) and patient portal (`apps/subject-portal`), presenting significant compliance risks.

This ADR defines the decisions to implement a standard npm compilation pipeline using Vite, remove Vite custom path aliases in favor of standard pnpm/node_modules workspace resolution, build a secure re-authentication modal with automated reactive memory credential hygiene, and establish a parameterized offline sync queue supporting multiple GxP conflict-resolution strategies.

## 2. Decision Drivers & Constraints
* **Compliance:** Strictly adhere to 21 CFR Part 11/EU Annex 11 requirements for electronic signatures (such as focus trapping and immediate credential erasure from reactive memory).
* **Maintainability & Workspace Boundaries:** Eliminate fragile Vite configurations and direct cross-workspace imports, enforcing structured boundaries via standard ESLint linting rules.
* **Traceability:** Aligns with core clinical specifications including PRD-SYS-001.

## 3. Options Considered
### Option 1: Inline Custom Integrations
* **Overview:** Maintain separate custom-crafted signature modals and sync loops in each application.
* **Pros:**
  * ✅ High isolation per application.
* **Cons:**
  * ❌ Massive code duplication.
  * ❌ Inconsistent GxP compliance enforcement across clinical vs. patient frontends.

### Option 2: Centralized Shared UI Library & Workspace Resolution
* **Overview:** Restructure `packages/ui` into a modern compiled package with ESM and CommonJS outputs, exporting fully audited compliant signature modals and sync adapters via standard pnpm workspace links.
* **Pros:**
  * ✅ Single source of truth for regulated compliance workflows.
  * ✅ Strict node_modules resolution avoids fragile build tool path hacks.
  * ✅ Enforced boundaries via automated ESLint rules preventing raw cross-workspace relative imports.
* **Cons:**
  * ❌ Requires a pre-build step for the shared library in CI/CD pipeline.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Centralizing the regulated logic under `packages/ui` and using standard node_modules workspace resolution guarantees consistent compliance enforcement, modular code reusability, and clean dependency management in accordance with GxP standards.

## 5. Consequences & Trade-offs
* **Positive Impact:** Regulated compliance rules for signature capture and offline synchronization are uniformly applied and easily audited.
* **Negative Impact / Technical Debt:** Added compile step (`pnpm build` under packages/ui) before running consuming applications.
* **Mitigation Strategy:** Configured rapid caching pipelines and automated workspace-wide build scripts to compile `packages/ui` transparently.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `packages/ui`, `apps/web`, `apps/subject-portal`
* **Verification Plan:** Verified standard ESM and CommonJS bundle emission under `packages/ui/dist/`. Ran and passed all unit and regression test suites globally across the entire monorepo using `pnpm test`. Enforced boundary validation with new ESLint flat config local-rules check.
