# ADR-253: Dedicated Site Administration Suite with RBAC Gating and Change Reason Guard

- **Status:** Accepted
- **Date:** 2026-08-08
- **Authors:** @google-labs-jules[bot]
- **Deciders:** @fderuiter, @google-labs-jules[bot]

---

## 1. Context & Problem Statement

To support centralized, compliant, and secure clinical trial site setups, organization management, and personnel directory provisioning, the Cadence Clinical platform requires a dedicated administrative workspace. 
Previously, administrators had to manage site configurations and staff directories through fragmented sub-tabs inside active clinical monitoring views or direct database updates. This mixed approach increased the risk of accidental clinical state corruption and violated strict compliance auditing boundaries.

To resolve these issues, we need a dedicated, decoupled Site Administration Suite under the `/admin` path. This suite must be isolated from the active clinical monitoring context, enforce strict role-based access control (RBAC), and guarantee that all state modifications undergo strict change justification gating before submission.

This decision implements requirements under PRD-SYS-001 and PRD-SYS-004.

## 2. Decision Drivers & Constraints

- **Compliance (FDA 21 CFR Part 11 / GxP):** Every administrative mutation (creating/updating sites, organizations, or personnel) must require a non-empty change justification, transmitted via the `X-Change-Reason` HTTP header and recorded in append-only logs.
- **Strict Role-Based Access Control:** Only users authorized with the normalized `sponsor_admin` role should be permitted to access the Site Administration workspace. All other roles must be blocked.
- **State Isolation:** Administrative choices and workflows must remain completely decoupled from the user's active clinical monitoring session (such as active `study_id` or `site_id` filters) to prevent state leaks or cross-contamination.
- **Codebase Design Principles:** Alignment with Python 3.14+ type checking, Ruff formatting guidelines, and Vue 3 + Pinia patterns.

## 3. Options Considered

### Option 1: Shared Workspace State (Embedded Tabs)

- **Overview:** Keep the administration forms inside existing operational workspaces (like the CTMS site view), using UI-level toggles to switch between clinical and administrative modes.
- **Pros:**
  - ✅ Simple router structure with no new paths.
- **Cons:**
  - ❌ Extremely high risk of session state leaks (e.g., active site selectors being corrupted).
  - ❌ Difficult to enforce strict role-based gateway route boundaries since the route itself is shared.

### Option 2: Decoupled Site Administration Suite under `/admin` (Selected)

- **Overview:** Implement a dedicated administration workspace at `/admin` backed by an isolated state store (`apps/web/src/stores/admin.js`) and protected by a robust Vue Router navigation guard checking for the `Sponsor Admin` role. Enforce a UI-level block that disables form submission until a change justification is provided.
- **Pros:**
  - ✅ Complete state isolation prevents session state pollution.
  - ✅ Gated router endpoints can be verified both on the frontend and gateway levels.
  - ✅ Explicitly enforces GxP compliance on all site modifications via form-level justification validation.
- **Cons:**
  - ❌ Requires a new Pinia store and additional frontend router configurations.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Option 2 is selected because it satisfies all 21 CFR Part 11 compliance standards, ensures strict data isolation, provides reliable RBAC protection on sensitive operations, and adheres to the decoupled architectural pattern of the Cadence Clinical platform.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - Clean separation of administrative operations from active clinical monitoring states.
  - 100% enforcement of change justifications on site, organization, and personnel directory mutations.
  - Comprehensive unit and integration test coverage across routing guards, state isolation, and UI change reason validations.
- **Negative Impact / Technical Debt:**
  - Slightly larger frontend bundle size due to the dedicated views and isolated store.

## 6. Implementation & Verification

- **Affected Repositories / Services:**
  - `apps/web/src/router/index.js` (Route protection & navigation guards)
  - `apps/web/src/components/AppShell.vue` (Conditional sidebar navigation rendering)
  - `apps/web/src/stores/admin.js` (Isolated Pinia store for organization management)
  - `apps/web/src/views/AdminView.vue` (Admin workspace interface with Change Reason Guard)
  - `apps/web/tests/admin_suite.test.js` (Frontend verification suite)
- **Verification Plan:**
  - Run the dedicated frontend tests using Vitest: `pnpm test admin_suite` to verify navigation guards, state isolation, and submission disablement.
  - Run backend organization directory tests: `uv run pytest apps/org/tests` to verify persistence, audit capture, and authorization.
