# ADR-2296: On-Demand Consent Status Validation and Audit Hooks

Also known as: ADR-121 (former number)

- **Status:** Accepted
- **Date:** 2026-07-31
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

The clinical execution service previously relied on a deprecated write endpoint (`/api/v1/execution/subjects/{subject_id}/consent`) to manually synchronize consent records. Removing this endpoint without a replacement would violate GxP validation rules, as the system must guarantee active consent before any clinical visit or observation data is written to PostgreSQL. Reference: PRD-SYS-001.

## 2. Decision Drivers & Constraints

- Guarantee real-time consent verification during transaction execution without relying on client-side compliance headers.
- 21 CFR Part 11 compliance requiring auditable write-blocking if consent is missing or subject requires re-consent.
- Avoid heavy message queue or push-based webhook architectures that introduce transactional race conditions.

## 3. Options Considered

### Option 1: Synchronous On-Demand Pull-Through Consent Cache & `before_flush` ORM Event Hooks (Selected)

- **Overview:** Synchronous On-Demand Pull-Through Consent Cache & `before_flush` ORM Event Hooks (Selected)
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead

### Option 2: Asynchronous Webhook Push Synchronization (Rejected — vulnerable to race conditions)

- **Overview:** Asynchronous Webhook Push Synchronization (Rejected — vulnerable to race conditions)
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead


## 4. Decision Outcome

- **Chosen Option:** Option 1. Implemented a `before_flush` SQLAlchemy listener in `apps/execution/database/audit.py` to inspect pending session mutations, execute on-demand consent status checks via `fetch_subject_consent_status`, update local `SubjectConsent` records, and raise `PermissionError` if consent is missing or invalid.
- **Justification:** Selected as primary architectural approach.

## 5. Consequences & Trade-offs

- Positive: Guarantees transactional safety and prevents invalid clinical observation commits.
- Negative: Introduces a synchronous network/cache lookup within database session flush cycles.

## 6. Implementation & Verification

- Modified `apps/execution/database/audit.py` and `apps/econsent/main.py`.
- Verification via `tests/test_reconsent_blocking.py`.
