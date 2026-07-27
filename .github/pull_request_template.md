# Pull Request Verification

## Description
Please describe the changes introduced by this pull request. Explain the business value, technical approach, and how it aligns with Cadence Clinical design system standards and core utility guidelines.

## Verification & Audit Checklist

To ensure a unified user experience, prevent UI duplication, and maintain strict cryptographic/utility standards, authors and reviewers must verify compliance against the following checklist before merging.

### 1. Reusable UI Components & Design System Audit
- [ ] **No Hardcoded UI Layouts:** I have verified that this change does not introduce custom, hardcoded HTML/CSS or duplicate layout markups (e.g., radio grids, visit matrices) in individual applications.
- [ ] **Shared UI Components:** All standard clinical inputs, radio grids, visit matrices, and rule editor containers are imported directly from the shared `ui` package.
- [ ] **Design System Compliance:** The component styling, responsiveness, and accessibility guidelines conform directly to Cadence Clinical standards.

### 2. Core Cryptographic & Utility Functions
- [ ] **Centralized Utilities:** I have verified that no duplicate/custom helper functions (e.g., SHA-256 hashing, gateway signatures, canonical serialization) have been introduced.
- [ ] **Shared Utility Imports:** All cryptographic signature and hashing functions are imported from the centralized `ui` package (re-exporting from `signing.js`).
- [ ] **21 CFR Part 11 Audit Trail Integrity:** Any ledger logging, audit stencils, or signature mechanisms utilize standardized, cryptographically validated block structures.

### 3. Unified Styling & Formatting Verification
- [ ] **Code Formatting Executed:** I have run the workspace-wide formatter (`pnpm format` / `pnpm -r format` and `uv run ruff format .`) recursively across all directories, including the patient-facing portal (`apps/subject-portal`).
- [ ] **Formatting Confirmed:** All modified and new files conform to ESLint, Prettier, and Ruff code formatting standards.

## Quality Assurance & Testing
- [ ] Workspace-wide tests (`pnpm test` and `uv run pytest`) have been executed locally and all logic checks pass successfully.
- [ ] New unit or integration tests have been added (under `tests/` or package-specific test directories) where applicable.
