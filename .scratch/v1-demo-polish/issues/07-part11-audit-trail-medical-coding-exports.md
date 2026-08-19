# 07: [Data Manager & Auditor] 21 CFR Part 11 Audit Trail, Medical Coding & Dataset Exports

**What to build:**
A regulatory compliance and quality workspace (`AuditView.vue`, `MedicalCodingView.vue`, `ExportWizardView.vue`, `DocumentManagerView.vue`) featuring searchable, cryptographically verified 21 CFR Part 11 audit trails, a MedDRA/WHODrug medical coding queue for adverse events, DIA Reference Model eTMF binder tree inspection, and one-click CDISC ODM XML / CSV data exports.

**Blocked by:**
- 05: [Site CRC] Dynamic Subject Enrollment & eCRF Visit Execution with Edit Checks
- 06: [CRA Monitor] CTMS Monitoring Console, SDV Toggles & Query Discrepancy Lifecycle

**Status:** ready-for-agent

## Context & User Story
As a Data Manager and Auditor, I want to search and filter audit trail records by user, entity, and timestamp with reasons for change, code uncoded adverse event verbatim terms against MedDRA dictionaries, inspect essential documents in the eTMF tree, and export verified CDISC ODM packages, so that clinical study quality and regulatory readiness are guaranteed.

## Acceptance Criteria
- [ ] Audit trail viewer supports filtering by date range, user ID, entity type (`Observation`, `Subject`, `Query`), and action (`CREATE`, `UPDATE`, `SIGN`).
- [ ] Every change row shows previous value, new value, reason for change, and electronic signature hash.
- [ ] Medical Coding Queue allows selecting an adverse event verbatim (e.g. "Severe cephalalgia") and auto-suggesting / confirming MedDRA PT ("Headache", LLT "Severe headache").
- [ ] eTMF viewer displays standardized DIA Reference Model zones (Trial Management, Regulatory, IRB/IEC, Site Management) with document inspection modals.
- [ ] Export Wizard generates valid CDISC ODM XML and CSV packages of all locked/unlocked study data.
- [ ] Tests in `apps/quality/tests/`, `apps/etmf/tests/`, and `apps/web/tests/test_audit.py` pass.
