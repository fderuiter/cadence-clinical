# 06: [CRA Monitor] CTMS Monitoring Console, SDV Toggles & Query Discrepancy Lifecycle

**What to build:**
A comprehensive Clinical Trial Management System (CTMS) monitoring workspace (`CtmsView.vue`, `CraVerificationConsole.vue`) featuring site KPI cards, subject matrix inspection, field-level Source Data Verification (SDV) status toggles, Delegation of Authority (DOA) log inspection, and end-to-end Query Discrepancy management (Issue Query $\rightarrow$ Site Answer $\rightarrow$ CRA Re-query/Close).

**Blocked by:** 05: [Site CRC] Dynamic Subject Enrollment & eCRF Visit Execution with Edit Checks

**Status:** ready-for-agent

## Context & User Story
As a CRA Monitor, I want to open Site 101 in the CTMS console, review subject visit data side-by-side with source records, toggle SDV checkmarks on verified fields, raise a query on an anomalous lab value with a specific reason, and verify the site staff's Delegation of Authority, so that data integrity is monitored in real-time according to ICH GCP E6(R2).

## Acceptance Criteria
- [ ] CTMS dashboard displays live KPIs: Total Subjects, Enrollment Rate, Verified SDV %, Open Queries count.
- [ ] `CraVerificationConsole.vue` allows inspecting eCRF forms with field-level SDV verification checkboxes.
- [ ] Raising a query on a form field creates a `QueryDiscrepancy` record in `OPEN` state and updates the field's visual badge to yellow.
- [ ] Site CRC can submit a response ("Value confirmed with medical record"), transitioning query to `ANSWERED`.
- [ ] CRA Monitor can mark query `CLOSED` with audit rationale.
- [ ] Delegation of Authority tab displays active site staff, training certificates, and delegated protocol roles.
- [ ] Tests in `apps/ctms/tests/` and `apps/web/tests/test_ctms.py` pass.
