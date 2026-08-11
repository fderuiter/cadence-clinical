# ADR-148: Centralized Auto-Generated API Client and Compatibility Wrappers

- Status: Accepted
- Date: 2026-08-11
- Authors: @jules
- Deciders: @fderuiter

---

## 1. Context & Problem Statement

Previously, API clients and request wrappers were duplicated across frontend applications and services, leading to inconsistencies and contract drift. Hand-written API integrations increased development friction and the likelihood of runtime failures due to schema changes. To address this, we need a centralized, auto-generated TypeScript/JavaScript API client package (`packages/shared-api-client`) that provides static typing, automatic client generation, and compatibility wrappers to maintain backward compatibility.

This decision implements requirements under PRD-CRF-006.

## 2. Decision Drivers & Constraints

- **Consistency:** Ensure a single source of truth for API clients across all workspaces.
- **Developer Velocity:** Automate API client generation to reduce manual coding and speed up integration.
- **Maintainability:** Support seamless backward compatibility through specialized API wrappers.

## 3. Options Considered

### Option 1: Manual Synchronization of API Clients

- **Overview:** Developers manually update API clients in each workspace when endpoints change.
- **Pros:**
  - ✅ Simple to understand and implement without additional tooling.
- **Cons:**
  - ❌ Highly prone to human error, inconsistencies, and API contract drift.

### Option 2: Centralized, Auto-Generated API Client with Compatibility Wrappers (Selected)

- **Overview:** Establish a dedicated package (`packages/shared-api-client`) containing an auto-generated API client generated from the OpenAPI specifications, complemented by compatibility wrappers for older endpoints.
- **Pros:**
  - ✅ Guarantees consistency and eliminates contract drift.
  - ✅ Dramatically improves developer productivity.
  - ✅ Compatibility wrappers prevent breaking changes on legacy codepaths.
- **Cons:**
  - ❌ Requires setup and maintenance of generator wrappers.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Option 2 provides a scalable, centralized architecture that scales with the platform. Auto-generating the API client prevents drift while compatibility wrappers ensure legacy code remains functional.

## 5. Consequences & Trade-offs

- **Positive Impact:** All API interactions are strictly typed, consistent, and automatically updated.
- **Negative Impact / Technical Debt:** Requires running the generator scripts as part of the build process.
- **Mitigation Strategy:** Provide automated scripts and clear documentation for client regeneration.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/shared-api-client`, `apps/web/`, `pyproject.toml`
- **Verification Plan:** Validated via automated workspace lint and check suites, as well as TypeScript definition verification.
