# ADR-2806: Country Multi-Site Greenlight Matrix Persistence

- **Status:** Accepted
- **Date:** 2026-10-02
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

The Site Startup (SSU) module requires persistent evaluation and batch certification of 6-facet greenlight activation readiness for trial sites across target countries (PRD-SSU-001, PRD-SSU-002, PRD-SSU-003). To store gate statuses, readiness metrics, and e-signature tokens in PostgreSQL, the `CtmsSiteGreenlightGate` entity needs an EF Core migration (`AddSiteGreenlightGates`) in the CTMS service database context.

## 2. Decision Drivers & Constraints

- **Driver 1:** Persistent storage of country-level 6-facet greenlight readiness and activation state.
- **Driver 2:** Compliance with 21 CFR Part 11 e-signature batch certification and audit logging (PRD-SSU-003).

## 3. Options Considered

### Option 1: EF Core Entity Mapping & PostgreSQL Migration (Selected)

- **Overview:** Add `CtmsSiteGreenlightGate` domain entity and corresponding EF Core `AddSiteGreenlightGates` migration in `apps/ctms/src/Infrastructure/Persistence/Migrations/`.
- **Pros:**
  - ✅ Fully persistent 6-facet readiness tracking and HMAC-SHA256 activation tokens
  - ✅ Satisfies PRD-SSU-001, PRD-SSU-002, PRD-SSU-003
- **Cons:**
  - ❌ Requires database schema updates for CTMS persistence

### Option 2: In-Memory / Transient State Only

- **Overview:** Evaluate greenlight gates dynamically without database persistence.
- **Pros:**
  - ✅ No schema migration needed
- **Cons:**
  - ❌ Fails Part 11 auditability and persistence requirements

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Chosen because it provides persistent, auditable 6-facet readiness tracking andPart 11 batch e-signature certification for country multi-site activation.

## 5. Consequences & Trade-offs

- **Positive Impact:** Full auditability and persistent activation states across multi-site clinical trials.
- **Negative Impact / Technical Debt:** Requires running EF Core migrations on CTMS service databases.
- **Mitigation Strategy:** Automated EF Core migrations and xUnit test suites verifying database queries.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/ctms`
- **Verification Plan:** Verified via `StartupGreenlightEndpointsTests.cs` and `MigrationUnitTests.cs`.
