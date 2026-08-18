# ADR-2184: Metadata-Bound Filtering and Consent Abstraction for Transaction Auditing

* **Status:** Accepted
* **Date:** 2026-08-18
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Transaction auditing in the execution microservice previously lacked strict metadata-bound entity filtering and an abstract interface for eConsent client verification. As a result, shared database sessions could attempt to audit un-audited or foreign entities, and cross-service eConsent checks were tightly coupled to concrete HTTP client implementations.

## 2. Decision Drivers & Constraints

* GxP 21 CFR Part 11 audit logging requirement (PRD-SYS-001) for execution entities.
* Microservice boundary isolation: execution audit handlers must inspect `AuditedModel` metadata rather than assuming all session-attached entities are execution audited models.
* Need for a decoupled, injectable `IConsentVerificationClient` port interface for subject consent status verification.

## 3. Options Considered

1. Metadata-bound entity filtering in transaction audit triggers + Abstract consent verification client port (Selected)
2. Direct class-name checks without metadata inspection + concrete client coupling

## 4. Decision Outcome

Chosen option: Option 1 because inspecting `AuditedModel` metadata guarantees safe entity filtering across shared ORM sessions, and `IConsentVerificationClient` provides a clean hexagonal port interface satisfying PRD-SYS-001.

## 5. Consequences & Trade-offs

* Positive: Safe transaction auditing without cross-service entity pollution; testable consent verification client interface.
* Negative: Small runtime metadata inspection overhead during session flushing.

## 6. Implementation & Verification

* Modified `apps/execution/database/audit.py`, `apps/execution/econsent_client.py`, `apps/execution/domain/ports.py`, and `apps/execution/application/ports.py`.
* Verification tests added under `apps/execution/tests/test_audit_metadata_filtering.py`.
