# ADR-120: SDV and TSDV Endpoint Hardening, RBAC Permission Mapping, and Local Study Scope Enforcement

* **Status:** Accepted
* **Date:** 2026-08-29
* **Authors:** @jules
* **Deciders:** @fderuiter, @jules

---

## 1. Context & Problem Statement
In order to transition toward a modern Vue SPA CRA Monitoring and SDV Workspace, the backend endpoints for Source Data Verification (SDV) and Targeted SDV (TSDV) need to be extracted from `apps/execution/main.py` into a modular router `apps/execution/routers/sdv.py`. Concurrently, coarse-grained role-based checks (e.g., checking for raw "CRA" or "Data Manager" strings) must be replaced with fine-grained RBAC permission checking (`Depends(require_permission("sdv:*"))`) and study-scoped visibility guards (`require_study_scope()`). This enforces 21 CFR Part 11 compliance and ensures secure data isolation across studies and roles.

This ADR specifically relates to requirements under **PRD-QRY-005**, **PRD-QRY-006**, and **PRD-QRY-007**.

## 2. Decision Drivers & Constraints
* **Driver 1:** Regulatory compliance (CRA monitor workspace access, site-isolation visibility metrics).
* **Driver 2:** Hardening security boundaries at the endpoint level by enforcing permission-scoped access rather than raw role memberships.
* **Driver 3:** Eliminating redundant or inline router code in favor of modular, decoupled service routing.
* **Constraint:** Must not break backward compatibility with the existing legacy REST endpoints and test suites.

## 3. Options Considered
### Option 1: Ad-hoc Check-Logic inside Main Execution Module
* **Overview:** Maintain inline REST routes and write manually embedded procedural checks to verify role memberships and study parameters on every request.
* **Pros:**
  * ✅ Requires no new modules or structural file layout modifications.
* **Cons:**
  * ❌ Highly error-prone and hard to maintain as more endpoints are added.
  * ❌ Violates separation of concerns and architecture patterns of the other routers (locks, documents, safety).
  * ❌ Inflexible when roles or permissions are refactored in core packages.

### Option 2: Modular Router Extraction with Finer-Grained RBAC & Local Study Scope Guards [Selected]
* **Overview:** Move all four SDV/TSDV endpoints and their associated Pydantic v2 schemas to `apps/execution/routers/sdv.py`. Implement granular permissions (`sdv:read`, `sdv:create`, `sdv:update`) and map them correctly inside `packages/security/rbac.py`. Integrate a local `StudyScopeChecker` / `require_study_scope()` that extracts `study_id` from route path parameters, query parameters, header keys, or JSON body payloads securely.
* **Pros:**
  * ✅ Extremely clean, testable, and robust routing layout mirroring other decoupled services.
  * ✅ Centralized permission matrix dictates access, resolving RBAC checks dynamically.
  * ✅ Avoids endpoint stream-reading bugs by safely cloning the HTTP request body stream.
* **Cons:**
  * ❌ Requires updating shared code in `packages/security/rbac.py`.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Option 2 guarantees secure design principles, matches repository routing conventions, and maintains robust Part 11 compliant audit and visibility trails.

## 5. Consequences & Trade-offs
* **Positive Impact:**
  * ✅ Endpoints are fully decoupled and self-contained.
  * ✅ Multi-transport study_id parsing handles all request patterns (headers, query, payload body, path).
  * ✅ Explicitly tested via comprehensive existing automated test coverage.
* **Negative Impact / Technical Debt:**
  * ❌ Introduces a small overhead in stream reading for POST endpoints, mitigated by non-destructive ASGI reset.

## 6. Implementation & Verification
* **Affected Repositories / Services:**
  * `apps/execution/main.py`
  * `apps/execution/routers/sdv.py`
  * `packages/security/rbac.py`
* **Verification Plan:**
  * Executed unit and persistence tests locally: `uv run pytest tests/test_sdv.py tests/test_tsdv.py tests/test_sdv_tsdv_persistence.py tests/test_rbac.py --no-cov`.
