# ADR-120: Shared Domain Vocabulary and Authorization Foundation

* **Status:** Accepted
* **Date:** 2026-08-28
* **Authors:** @jules
* **Deciders:** @lead-architect, @qa-validator

---

## 1. Context & Problem Statement
To establish the security and directory isolation boundaries for site staff delegation of authority, we need a unified vocabularly of clinical roles, organization types, and significant duties alongside role/delegation-aware authorization primitives. We need to prevent role scoping bypasses and ensure only authorized role configurations can delegate duties or execute actions on behalf of site/sponsor entities under FDA 21 CFR Part 11 and ICH E6(R2) compliance requirements.

This ADR specifically relates to requirements under **PRD-SYS-001**.

## 2. Decision Drivers & Constraints
* **Driver 1:** Enforcing separation of duties and roles in accordance with ICH E6(R2) significant trial-related duties.
* **Driver 2:** Compliance with GxP and FDA 21 CFR Part 11 electronic records.
* **Driver 3:** Developer velocity and reuse of consolidated audit metadata structures across microservices.

## 3. Options Considered
### Option 1: Ad-hoc local checks in each microservice
* **Overview:** Implement custom dictionaries and role gate validations inside each router individually.
* **Pros:**
  * ✅ Quick to implement for isolated endpoints.
* **Cons:**
  * ❌ Leads to duplication, lack of global vocabulary alignment, and susceptibility to authorization bypasses.

### Option 2: Shared Domain Vocabulary and Centralized Authorization Helpers
* **Overview:** Define shared enums and Pydantic validation structures in core package libraries, accompanied by reusable, dependency-injectable FastAPI role/delegation check utilities.
* **Pros:**
  * ✅ Absolute consistency across services.
  * ✅ Robust, tested authorization primitives preventing security drifts.
  * ✅ Clean-room consolidation of auditing metadata via `AuditMixin`.
* **Cons:**
  * ❌ Requires upfront library structural design and publishing across package boundaries.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Choosing Option 2 guarantees that all downstream clinical trial services adhere to the identical role schemas and authorization policies, satisfying strict regulatory validation gates.

## 5. Consequences & Trade-offs
* **Positive Impact:** Secure, uniform role verification and auditing metadata across the Organization Directory and CTMS services.
* **Negative Impact / Technical Debt:** Future role additions require updating the shared package library enums.
* **Mitigation Strategy:** Enums are designed with alias definitions to remain highly extensible.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `packages/core-models`, `packages/database`, `packages/security`
* **Verification Plan:**
  * Automated unit and integration tests written in `tests/test_organization_domain.py` and `tests/test_delegation.py`.
