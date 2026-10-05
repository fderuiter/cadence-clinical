# ADR-2805: CTMS Site Startup Greenlight Activation Matrix

- **Status:** Accepted
- **Date:** 2026-10-02
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Clinical trial site activation requires rigorous multi-facet verification across regulatory IRB approval, CTA budget execution, eISF baseline setup, DOA log completion, staff protocol training, and IP release. A centralized site startup greenlight matrix is required in CTMS to track readiness and issue cryptographically verifiable activation tokens.

## 2. Decision Drivers & Constraints

- **Driver 1:** Regulatory compliance (21 CFR Part 11 / GxP) for site activation certification (PRD-SSU-001, PRD-SSU-002, PRD-SSU-003).
- **Driver 2:** Automated multi-facet readiness tracking and HMAC-SHA256 activation token generation.

## 3. Options Considered

### Option 1: CTMS Site Greenlight Gate Entity and API Endpoints (Selected)

- **Overview:** Add `CtmsSiteGreenlightGate` entity in CTMS persistence with EF Core migration, providing single-site and batch country-level certification endpoints.
- **Pros:**
  - ✅ Satisfies requirements PRD-SSU-001, PRD-SSU-002, PRD-SSU-003
  - ✅ Provides tamper-evident HMAC-SHA256 greenlight activation tokens
- **Cons:**
  - ❌ Requires database migration in CTMS schema

### Option 2: Unverified Manual Site Activation

- **Overview:** Allow sites to be flagged active without multi-facet checks.
- **Pros:**
  - ✅ Simpler implementation
- **Cons:**
  - ❌ Non-compliant with GxP site startup requirements

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Chosen because it provides verifiable, audited multi-facet site activation gates.

## 5. Consequences & Trade-offs

- **Positive Impact:** Full regulatory compliance and automated site readiness calculation.
- **Negative Impact / Technical Debt:** Additional database table and migration maintenance.
- **Mitigation Strategy:** Automated test coverage in `StartupGreenlightEndpointsTests.cs`.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/ctms`
- **Verification Plan:** Unit tests in `StartupGreenlightEndpointsTests.cs` and EF Core migration `AddStartupGreenlight`.
