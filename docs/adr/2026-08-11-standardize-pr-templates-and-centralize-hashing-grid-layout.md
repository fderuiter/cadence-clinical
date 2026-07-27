# ADR-85: Standardize PR Templates and Centralize Shared Hashing & Radio Grid Layouts

* **Status:** Accepted
* **Date:** 2026-08-11
* **Authors:** @google-labs-jules[bot]
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement
Previously, the absence of standardized guidelines and review processes allowed duplicate markup and redundant utility functions to slip into codebase applications (such as the patient/subject portal and main web app). This fragmentation weakened our UI consistency, deviated from our design system standards, and introduced maintenance overhead via duplicate cryptographic implementations. Specifically, different applications implemented separate, redundant SHA-256 helpers and generated inline HTML structures for clinical radio grids.

## 2. Decision Drivers & Constraints
* **Compliance & Standard Guidelines:** GxP/FDA Part 11 requirements demand robust, traceable processes during SDLC.
* **Consistency:** Ensure UI layout consistency across multiple platform apps (subject-portal, web).
* **Maintainability:** Reduce maintenance overhead by centralizing utility modules and leveraging shared packages (`packages/ui`).

## 3. Options Considered
### Option 1: Status Quo (Decentralized implementations)
* **Overview:** Maintain application-level duplication of hashing helpers and HTML rendering templates.
* **Pros:**
  * ✅ No coordination across application and packages boundaries.
* **Cons:**
  * ❌ Higher maintenance overhead and security risk from differing cryptographic implementations.
  * ❌ Direct violation of GxP standards around change management and software consistency.

### Option 2: Centralize Shared Components and Cryptographic Utilities under Shared packages
* **Overview:** Centralize SHA-256 helper in `packages/ui` (and export from entry point) and clinical radio grid markup generation, using shared package imports in web and subject portals.
* **Pros:**
  * ✅ Single point of truth for critical cryptographic hashing and standard form layouts.
  * ✅ Seamless recursive Prettier and Ruff linting/formatting workspace enforcement.
  * ✅ Clearer SDLC and requirements traceability (PR templates checklist).
* **Cons:**
  * ❌ Requires package rebuilds and proper dependency workspace resolution.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Centralization ensures cryptographic consistency, uniform UI rendering, and full conformance with GxP platform guidelines.

## 5. Consequences & Trade-offs
* **Positive Impact:** 100% deduplication of cryptography modules across portal boundaries. Workspace-wide uniform lint/format checks.
* **Negative Impact / Technical Debt:** Minimal. Applications must declare dependencies on `packages/ui` rather than remaining standalone.
* **Mitigation Strategy:** Automated workspaces package configuration checks.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `packages/ui`, `apps/subject-portal`, `apps/web`.
* **Verification Plan:** Verified via standard Vitest unit tests in `packages/ui/tests/signing.test.js` and `apps/subject-portal/tests/portal.test.js`, alongside workspace lint and Cypress/Playwright execution flows.
