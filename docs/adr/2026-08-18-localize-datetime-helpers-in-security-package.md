# ADR-2184: Localize datetime helpers in security package

* **Status:** Accepted
* **Date:** 2026-08-18
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

To support standalone sidecar container builds and isolate dependencies across microservices and packages (such as `packages/deid`), `packages/security` previously depended on `packages/database` for utility functions such as `get_utc_now()` and ISO datetime formatting helpers. This created a circular or unwanted packaging dependency loop between `packages/security` and `packages/database`.

Reference requirement: PRD-SYS-001.

## 2. Decision Drivers & Constraints

* Break cross-package packaging dependency loop between security and database packages during container sidecar builds.
* Maintain microservice and package build isolation without requiring database package dependencies in lightweight sidecar containers.
* GxP compliance for accurate UTC timestamp generation in security audit logs and cryptographic signature verifications.

## 3. Options Considered

1. Option A: Localize `get_utc_now()` and ISO datetime parsing/formatting helpers directly within `packages/security/datetime_helpers.py`. (Selected)
2. Option B: Retain dependency on `packages/database` and pull database models and session dependencies into sidecar containers. (Rejected)

## 4. Decision Outcome

Chosen option: Option A. Localizing datetime utility functions inside `packages/security/datetime_helpers.py` allows `packages/security` and downstream packages (such as `packages/deid`) to operate in build isolation without pulling in database dependencies.

## 5. Consequences & Trade-offs

* Positive: Clean build isolation for `packages/security` and sidecar containers without cross-package dependency cycles.
* Positive: Zero runtime overhead or changes to cryptographic signature verification logic.
* Negative: Minor duplication of utility functions across `packages/database` and `packages/security`.

## 6. Implementation & Verification

* Created `packages/security/datetime_helpers.py` containing ISO timestamp formatting and `get_utc_now()` utility functions.
* Updated `packages/security/audit_logger.py`, `crypto_verifier.py`, and `signature.py` to import from local datetime helpers.
* Verified via quality gates and unit tests (`uv run python scripts/validate_adrs.py` and `uv run cadence check --parallel`).

