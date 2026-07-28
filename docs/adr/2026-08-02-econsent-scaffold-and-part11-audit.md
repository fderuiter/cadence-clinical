# ADR-053: eConsent Scaffold and Shared Part 11 Audit Schemas

* **Status:** Accepted
* **Date:** 2026-08-02
* **Authors:** @jules
* **Deciders:** @fderuiter, @jules

---

## 1. Context & Problem Statement
The Cadence Clinical platform is introducing Electronic Consent (eConsent) features to support modern digitized, clinical trial recruitment and compliance. In order to comply with FDA 21 CFR Part 11 auditing regulations, avoid database schema leakage across bounds, and provide a unified foundation for all services handling audited metadata, we need to create a dedicated FastAPI microservice package (`apps/econsent/`) with its own database persistence layer, register gateway authentication middleware, and centralize Pydantic v2 reusable audit fields within the core models library (`packages/core-models/`).

This decision implements requirements under Trace-6.

## 2. Decision Drivers & Constraints
* **Driver 1 (Compliance):** FDA 21 CFR Part 11 requires strict, immutable audit trails. All mutations must require a non-empty change justification, track the creating identity, record chronological timestamps, and track version increments.
* **Driver 2 (Database Decoupling):** eConsent schemas and database connections must be isolated from execution and CTMS databases to guarantee high cohesion and prevent security/schema leaking.
* **Driver 3 (DRY Schema Design):** Avoid duplicate Pydantic definitions for standard Part 11 fields across microservices (e.g., eConsent, Organization Directory) by housing them in a shared core models module.

## 3. Options Considered
### Option 1: Inline Audit Fields in each Service Schema
Define standard audit and metadata fields manually in each service's Pydantic models.
- Pros:
  * ✅ No dependency on standard package libraries.
- Cons:
  * ❌ Violates DRY principles.
  * ❌ Inconsistent validation rules (e.g., whitespace and empty checks for reason justification).

### Option 2: Shared Pydantic v2 Audit Base Schema (Selected)
Define a reusable `AuditFields` model/mixin in `packages/core-models/audit.py` that can be imported by any microservice, including eConsent and Organization Directory.
- Pros:
  * ✅ Standardizes FDA 21 CFR Part 11 validation (e.g. non-empty `reason_for_change` validation).
  * ✅ Clean schema reuse across request/response models.
  * ✅ Standalone, importable eConsent microservice.
- Cons:
  * ❌ Introduces a slight shared dependency on `core-models`.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Option 2 guarantees that all compliance-sensitive services use identical, thoroughly validated, and strict Part 11 audit fields. The eConsent service is clean, modular, and leverages this shared foundation for its API requests and responses.

## 5. Consequences & Trade-offs
* **Positive Impact:** Strict, automated validation of change justifications on any mutation. Easy to scaffold new endpoints with identical auditing footprints.
* **Negative Impact / Technical Debt:** Requires keeping `packages/core-models` updated when schema structures evolve.
* **Mitigation Strategy:** Keep `packages/core-models/` minimal and focused on standard structural definitions with high test coverage.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `apps/econsent/`, `packages/core-models/`
* **Verification Plan:** Verify complete database initialization, secure `GatewayAuthMiddleware` context variable propagation, health endpoints, and strict validation checks on whitespace change reasons.
