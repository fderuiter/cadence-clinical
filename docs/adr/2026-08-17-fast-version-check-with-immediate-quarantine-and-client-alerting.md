# ADR-2179: Fast Version-Check with Immediate Quarantine and Client Alerting

- **Status:** Accepted
- **Date:** 2026-08-17
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirement:** PRD-SYS-001

---

## 1. Context & Problem Statement

In clinical research platforms, maintaining data integrity during offline and asynchronous data acquisition is critical. During offline ePRO (electronic Patient-Reported Outcomes) data entries, participant devices might submit data using a version of an instrument different from the active version configured in the central database. If unchecked, attempting to reconcile outdated schemas into active clinical registries can lead to severe data corruption or validation failures.

To solve this, we implement an immediate, fast version-index comparison check (`_resolve_and_save_submission`) as a gateway gate before reconciliation. Payloads with mismatching versions are diverted to a dedicated quarantine registry, preventing database corruption while preserving the patient's raw inputs for subsequent manual intervention/replay in accordance with FDA 21 CFR Part 11 and GxP standards.

## 2. Decision Drivers & Constraints

- **Clinical Data Integrity & GxP Standards:** Outdated or mismatched payloads must never pollute active clinical database registers.
- **Traceability & Regulatory Auditing (PRD-SYS-001):** The system must generate compliant audit logs (`write_audit_log` with `EPRO_QUARANTINED`) specifying the mismatch details.
- **Graceful Client Recovery:** Client devices must be notified of quarantined status to automatically suspend background synchronization and prompt users to update/refresh configs without failing the overall sync pipeline.

## 3. Options Considered

### Option 1: Reject and Return HTTP 400 Errors

- **Overview:** Reject mismatching payloads immediately and drop the sync connection.
- **Pros:**
  - ✅ Simple implementation.
- **Cons:**
  - ❌ Severe data-loss risk; raw patient-submitted responses could be lost on retry limits.
  - ❌ Blockages in multi-record queue syncing.

### Option 2: Quarantine, Compliant Audit Logging, and Suspension (Selected)

- **Overview:** Route outdated submissions to `EPROSubmissionQuarantine`, log GxP-compliant `EPRO_QUARANTINED` audit entries, return `"QUARANTINED"` in sync loops, and flag local devices to suspend auto-sync and display user prompt warning banners.
- **Pros:**
  - ✅ Eliminates clinical database corruption risks completely.
  - ✅ Preserves raw clinical inputs securely for eventual manual correction/replay.
  - ✅ Complies fully with PRD-SYS-001.
- **Cons:**
  - ❌ Requires specialized quarantine tables and administrative replay tools.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Option 2 is chosen because it successfully prevents database corruption while fully preserving raw clinical input data and ensuring strict GxP traceability (PRD-SYS-001) without disrupting the synchronization of unrelated valid payloads.

## 5. Consequences & Trade-offs

- **Positive Impact:** Fail-safe clinical data isolation, automatic participant prompting via localized IndexDB configuration checks, and standard audit trail tracking.
- **Negative Impact / Technical Debt:** Added storage requirements for quarantined payloads and a necessity for a backend administrative replay route.
- **Mitigation Strategy:** Automated background cleanup routines for older resolved quarantined logs, and complete integration test validation.

## 6. Implementation & Verification

- **Affected Repositories / Services:**
  - `apps/execution/` (Quarantine storage models and routing context)
  - `apps/interop/` (Version gateway validation check)
  - `apps/subject-portal/` (IndexedDB version capture, sync suspend logic, and UI warning prompt)
- **Verification Plan:**
  - Validated through integration pipeline test `test_epro_version_mismatch_quarantine_pipeline` confirming correct routing and audit log generation under PRD-SYS-001.
