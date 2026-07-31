# ADR-117: GxP 21 CFR Part 11 eConsent Audit Trails

* **Status:** Accepted
* **Date:** 2026-07-31
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Patient consent is legally and ethically mandatory prior to any protocol procedures in clinical trials. Under GxP and 21 CFR Part 11 regulations, patient eConsent records must be immutable, securely captured, and bound to the exact active Informed Consent Form (ICF) version index. We need to implement an audit verification system inside clinical execution to prevent any deletions or modifications of signed consent records, store high-resolution signature SVG vector data, identity verification details, and automatically update subject consent statuses to `RECONSENT_REQUIRED` upon protocol/ICF amendments.

Traced requirement: PRD-SYS-001.

## 2. Decision Drivers & Constraints

* Strict GxP and 21 CFR Part 11 regulatory compliance.
* Absolute immutability of signature records and consent audits once finalized.
* Seamless automatic transition of subject status when a protocol or ICF version is amended.

## 3. Options Considered

1. **Database and SQLAlchemy Event-Level Immutability (Chosen)**: Register event listeners on models `ConsentFormRecord` and `ConsentSignature` in `apps/execution/database/models.py` to block updates/deletes before flush, coupled with a dedicated `EConsentService` to manage state changes safely.
2. **REST API Gateway Validation Only**: Enforce compliance solely at the API gateway or endpoint level. This option is less secure because direct service calls or downstream code modifications could bypass verification.

## 4. Decision Outcome

Chosen option: Option 1. Immutability is enforced directly at the SQLAlchemy ORM model level, meaning no process or code can modify or delete a signed consent signature/record without raising a database-level transaction error. This guarantees high-integrity 21 CFR Part 11 compliance.

## 5. Consequences & Trade-offs

* **Positive**: Absolute protection of clinical data and metadata. Meets the highest regulatory qualification standards.
* **Positive**: Automatic and atomic status updates to `RECONSENT_REQUIRED` for affected subjects when ICF versions are updated.
* **Negative**: Requires careful mock setups in tests to satisfy the strict state machine sequence transitions.

## 6. Implementation & Verification

* **Models Modified**: `ConsentFormRecord` and `ConsentSignature` defined in `apps/execution/database/models.py`.
* **Services Added**: `EConsentService` implemented in `apps/execution/services/econsent_service.py`.
* **State Machine Modified**: `SubjectState` updated to support `RECONSENT_REQUIRED` in `apps/execution/subject_lifecycle.py`.
* **Verification Tests**: Automated tests added under `tests/test_econsent_workflow.py` validating successful signing, protocol amendment gating, and immutability violations.
