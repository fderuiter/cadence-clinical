# ADR-117: GxP Compliance Change Request Workflow

* **Status:** Accepted
* **Date:** 2026-08-26
* **Authors:** @fderuiter
* **Deciders:** @fderuiter, @architect-lead

---

## 1. Context & Problem Statement
In clinical trials and GxP 21 CFR Part 11 regulated environments, changing system settings (e.g. password policies, eSignature timeout thresholds, site isolation rules, data lock configurations) requires an auditable multi-approver Change Request workflow. Every setting modification must record non-repudiable user signatures, impact assessments, and diff logs.

This decision implements requirements under PRD-SYS-001.

## 2. Decision Drivers & Constraints
* **Driver 1 (21 CFR Part 11 Compliance):** Must maintain non-repudiable electronic signatures and dual-approval thresholds.
* **Driver 2 (Automated Impact Assessment):** Automated diff report categorizing changes by clinical risk (LOW, MEDIUM, HIGH).
* **Driver 3 (Audit Logging):** Full audit logs with pre-change and post-change value capture.

## 3. Options Considered

### Option 1: Inline setting modification with single sign-off
Directly mutate settings with a single administrator signature and log the event.
* **Pros:**
  * ✅ Simpler codebase footprint.
* **Cons:**
  * ❌ Violates 21 CFR Part 11 multi-approver requirements.

### Option 2: Structured Compliance Change Request Workflow
Implement dual-approver `ComplianceChangeRequest` and `ChangeApprovalSignature` models.
* **Pros:**
  * ✅ Enforces multi-approver threshold before settings are applied.
  * ✅ Preserves unique cryptographic/electronic signature tokens.
  * ✅ Generates automated impact assessments.
* **Cons:**
  * ❌ Higher database schema complexity.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Option 2 meets all GxP regulatory requirements for clinical software and 21 CFR Part 11 guidelines.

## 5. Consequences & Trade-offs
* **Positive Impact:** Full traceability of system-level configuration changes.
* **Negative Impact / Technical Debt:** Additional database tables and relationships to maintain.
* **Mitigation Strategy:** Automated unit and integration tests to verify workflow and non-repudiation.

## 6. Implementation & Verification
* **Affected Repositories / Services:**
  - `apps/execution/database/models.py`
  - `apps/execution/services/change_request_service.py`
* **Verification Plan:**
  - Run the test suite `tests/test_compliance_change_request.py` to verify the multi-approver threshold and audit logs.
