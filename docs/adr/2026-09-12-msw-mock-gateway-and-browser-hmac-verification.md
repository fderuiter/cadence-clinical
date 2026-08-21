# ADR-2185: MSW Mock Gateway Simulator and Browser HMAC Verification Engine

- **Status:** Accepted
- **Date:** 2026-09-12
- **Authors:** @jules
- **Deciders:** @fderuiter, @cadence-arch-board
- **Requirements:** PRD-GWY-001

---

## 1. Context & Problem Statement

The Cadence platform enforces strict GxP 21 CFR Part 11 electronic signature verification and API Gateway security controls across microservice boundaries. During local frontend development, end-to-end component testing, and MSW (Mock Service Worker) offline simulation, web components need to emulate Gateway path prefix stripping, 403 Subject-role route access restrictions, and dual-signature header validation (specifically `X-Sig-Token` step-up signature tokens and `X-Gateway-Signature` HMAC-SHA256 headers).

Prior to this change, client-side MSW mock handlers lacked centralized browser-compatible HMAC-SHA256 signing and path-prefix validation routines, causing mock responses to diverge from real Gateway security behavior.

## 2. Decision Drivers & Constraints

- **Driver 1:** Parity with API Gateway security controls (`X-Sig-Token`, `X-Gateway-Signature` Version 2, Subject 403 route blocking, path prefix stripping).
- **Driver 2:** Browser Web Crypto API compatibility (`crypto.subtle`) for client-side MSW handlers without Node-only crypto module dependencies.
- **Driver 3:** Zero regression in existing frontend unit and integration test suites.

## 3. Options Considered

### Option 1: Ad-hoc Mock Signature Checking in Individual Handler Files

- **Overview:** Implement custom validation logic inside individual MSW route handlers.
- **Pros:**
  - ✅ Quick to throw together for single tests.
- **Cons:**
  - ❌ Duplicate code and inconsistent signature enforcement across endpoints.
  - ❌ Hard to maintain when Gateway signature rules evolve.

### Option 2: Shared `mockGateway` Simulator in `@cadence/ui` (Selected)

- **Overview:** Create a centralized `mockGateway.js` module in `packages/ui` exporting path-prefix validators, Subject-role whitelist checkers, and `validateGatewayRequest` utilizing browser Crypto APIs via `signing.js`.
- **Pros:**
  - ✅ Centralized, reusable Gateway security simulation for MSW and client test runners.
  - ✅ Full alignment with API Gateway Version 2 signature spec and Subject RBAC rules.
  - ✅ Unit tested via Vitest in `packages/ui/tests/mockGateway.test.js`.
- **Cons:**
  - ❌ Requires exposing `mockGateway` export entry in `packages/ui/package.json`.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Centralizing Gateway simulation in `@cadence/ui/mockGateway` guarantees consistent security and RBAC validation across all client-side MSW mock interceptors and tests.

## 5. Consequences & Trade-offs

- **Positive Impact:** MSW mock handlers accurately mirror Gateway behavior (prefix stripping, 403 Forbidden for Subject accessing administrative routes, e-signature gating).
- **Negative Impact / Technical Debt:** Added export point `mockGateway` in `packages/ui/package.json`.
- **Mitigation Strategy:** Covered by dedicated Vitest unit tests in `packages/ui/tests/mockGateway.test.js`.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`
- **Verification Plan:** `pnpm --filter @cadence/ui test` and `uv run python scripts/validate_adrs.py`.
