# ADR-132: Vue SPA Foundation and Tooling Setup

- **Status:** Accepted
- **Date:** 2026-08-30
- **Authors:** @jules
- **Deciders:** @jules

---

## 1. Context & Problem Statement

Transitioning the platform's client-side user experience to a fully dynamic Single Page Application (SPA) necessitates establishing a foundational Vue 3 architecture, OIDC authentication with Keycloak, and client-side RBAC. We need to document these architectural choices, workspace conventions, and tooling decisions (Vite, Vitest, ESLint, and Pinia) referencing our modular pnpm workspace established in [2026-07-22-pnpm-frontend-workspace.md](2026-07-22-pnpm-frontend-workspace.md) as precedent.

This decision implements requirements under Trace-8.

## 2. Decision Drivers & Constraints

- **Driver 1 (User Experience & Reactivity):** Clinical users require interactive, highly responsive, and localized user interfaces that facilitate instantaneous feedback and client-side validation without complete page reloads.
- **Driver 2 (Compliance & Security Boundaries):** In alignment with FDA 21 CFR Part 11 and GxP standards, access to specific routes must be gated based on user roles and permissions, though actual security enforcement must remain API-side.
- **Driver 3 (Modular Workspace Design):** Maintain clean separation between workspace modules (`apps/web`, `packages/ui`) to enable rapid component sharing and isolated verification.

## 3. Options Considered

### Option 1: Vue 3 SPA with Vue Router, Pinia, and keycloak-js (Selected)

Replace static page elements with a comprehensive Vue 3 Single Page Application using standard Vue ecosystem packages.

- **Pros:**
  - ✅ Highly performant, extremely reactive, and lightweight bundle footprint.
  - ✅ Declarative routing setup with custom route meta tags simplifies role checks.
  - ✅ Standard keycloak-js adapter integrates natively with the platform's Keycloak realm.
- **Cons:**
  - ❌ Requires managing client-side navigation fallbacks for static deployments.

### Option 2: Server-Side Rendered (SSR) Multi-Page Application

Generate and render views on the server and use simple interactive islands.

- **Pros:**
  - ✅ Simpler initial load times and no client-side routing complexity.
- **Cons:**
  - ❌ Reduced client-side responsiveness, and higher backend resource consumption per request.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Vue 3 coupled with Pinia and Vue Router provides the optimal developer velocity, execution performance, and workspace-level integration. This setup matches the modular pnpm workspace standards.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - ✅ Modern, maintainable, and standard-compliant single-page application foundation.
  - ✅ Secure authorization-code flow with PKCE via keycloak-js.
  - ✅ Isolated component and state testing via Vitest and Happy DOM / JSDOM.
- **Negative Impact / Technical Debt:**
  - ❌ Multi-package pnpm layout requires maintaining clean dependency separation between `packages` and `apps`.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/web/`
- **Verification Plan:**
  - Execute `pnpm --filter web build` to ensure error-free compilation and bundle optimization.
  - Ensure `pnpm -r lint` and `pnpm -r test` pass cleanly to verify correct ESLint parsing of single-file components and DOM environment test execution.
