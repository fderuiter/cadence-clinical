# ADR-188: Clinical Rules and Query Designer Tabs

- **Status:** Accepted
- **Date:** 2026-09-02
- **Authors:** @jules
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

To support production-ready Vue.js single page applications (SPA) in Phase 25, clinical trial workspaces require seamless integration of role-specific workflows (CRC, CRA, Data Manager, and TMF Auditor) alongside standardized CDISC metadata-driven elements. Under this architectural layout, ESLint rules enforce strict `no-unused-vars` constraints on all exported shared UI elements. This ADR documents the architectural decision of modernizing shared helper files in `packages/ui/` to support clean tab navigations and query panel permissions while complying with strict ESLint requirements.

This decision addresses the following requirements:

- **PRD-SYS-001:** Cryptographic Audit Ledger & Compliance Logging
- **PRD-CRF-001:** CRF metadata-driven rendering
- **PRD-QRY-001:** Centralized query lifecycle states

## 2. Decision Drivers & Constraints

- **Driver 1:** ESLint compliance and strict GxP CI/CD static analysis.
- **Driver 2:** Support role-gated permissions where clinical query actions (creation, resolution, reopening) are restricted to monitors (CRA) and data managers.
- **Driver 3:** Backwards compatibility with the unified DDF/USDM schema.

## 3. Options Considered

### Option 1: Disable ESLint rules on shared packages

- **Overview:** Disable `no-unused-vars` inside `packages/ui` configuration.
- **Pros:**
  - ✅ Avoids rewriting exported helper files.
- **Cons:**
  - ❌ Violates GxP coding standards and quality gates.

### Option 2: Modernize parameter signatures and document unused variables

- **Overview:** Prefix unused forms parameters or add `eslint-disable-next-line` markers to preserve public API signatures while silencing static warnings cleanly.
- **Pros:**
  - ✅ Preserves the public API contract for existing callers.
  - ✅ Fully satisfies CI/CD ESLint constraints.
- **Cons:**
  - ❌ Adds minimal boilerplate comments.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Option 2 preserves backward compatibility of public function signatures in `packages/ui/index.js` while ensuring ESLint rules are fully respected across the workspace.

## 5. Consequences & Trade-offs

- **Positive Impact:** All linting checks pass cleanly, and code quality is preserved.
- **Negative Impact / Technical Debt:** Minimal use of eslint-disable directives.
- **Mitigation Strategy:** Continue tracking clean, unused variables across the shared frontend framework.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui/`
- **Verification Plan:** Verify utilizing `pnpm --filter ui lint` and running `uv run python scripts/validate_adrs.py`.
