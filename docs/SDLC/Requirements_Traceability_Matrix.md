# Requirements Traceability Matrix (RTM)

*Generated on:* 2026-07-23 22:04:08 UTC
*Regulatory Compliance Standards:* FDA 21 CFR Part 11, EU Annex 11, GAMP 5, IEC 62304 Section 5.7 & 5.8

## 1. Traceability Summary

- **Total Documented Requirements:** 38
- **Total Mapped to Automated Tests:** 9
- **Traceability Coverage:** 23.7%
- **SRS Requirements Mapped:** 2 of 3 (66.7%)

⚠️ **WARNING:** SRS coverage is below 100%. GxP validation requires 100% of functional requirements defined in the SRS to map to automated test cases.

## 2. Requirements Mapping Table

| Requirement ID | Source Document | Title / Description | Mapped Test Cases | Status |
| :--- | :--- | :--- | :--- | :--- |
| PRD-EDC-001 | PRD | **Spreadsheet Ingestion Sheet Structure** | *None* | ❌ **Unmapped** |
| PRD-EDC-002 | PRD | **Field-Level Ingestion Validations** | *None* | ❌ **Unmapped** |
| PRD-EDC-003 | PRD | **Dynamic Skip Logic Evaluation** | *None* | ❌ **Unmapped** |
| PRD-EDC-004 | PRD | **Cascading Dependent Nullification (Orphan Data Safeguard)** | *None* | ❌ **Unmapped** |
| PRD-EDC-005 | PRD | **Real-Time Row Ingestion and Index Tracking** | *None* | ❌ **Unmapped** |
| PRD-EDC-006 | PRD | **Advanced Inputs (VAS and Interactive Body Maps)** | *None* | ❌ **Unmapped** |
| PRD-EDC-007 | PRD | **Local IndexedDB Security & State Preservation** | *None* | ❌ **Unmapped** |
| PRD-EDC-008 | PRD | **Conflict Resolution and Sync Reconciliation** | *None* | ❌ **Unmapped** |
| PRD-EDC-009 | PRD | **Visual Analog Scale (VAS) Slider Specifications** | *None* | ❌ **Unmapped** |
| PRD-EDC-010 | PRD | **Interactive Body Map Coordinates and Schema Mapping** | *None* | ❌ **Unmapped** |
| PRD-MDR-001 | PRD | **Value-Level Metadata Constraint Propagation** | `test_terminology_cache_prevents_db_queries` (tests/test_transformers.py) 🟢<br>`test_usdm_validation_error_on_invalid_data` (tests/test_transformers.py) 🟢 | ✅ **Passed** |
| PRD-MDR-002 | PRD | **Biomedical Concept Lock State during Active Studies** | *None* | ❌ **Unmapped** |
| PRD-MDR-003 | PRD | **Dynamic Cohort Opening & Closing Rules** | `test_usdm_endpoint_returns_nested_schema_and_fast` (tests/test_transformers.py) 🟢 | ✅ **Passed** |
| PRD-MDR-004 | PRD | **Crossover Timeline Mapping & Arm Interventions** | `test_usdm_endpoint_returns_nested_schema_and_fast` (tests/test_transformers.py) 🟢 | ✅ **Passed** |
| PRD-MDR-005 | PRD | **Dual-Key Blinding Security** | `test_key_splitting` (tests/test_cryptography.py) 🟢<br>`test_encryption_decryption_with_rotation` (tests/test_cryptography.py) 🟢 | ✅ **Passed** |
| PRD-MDR-006 | PRD | **Blinding Constraints on UI Data Rendering** | *None* | ❌ **Unmapped** |
| PRD-MDR-007 | PRD | **Logical Mapping of I/E Criteria to eCRF Fields** | *None* | ❌ **Unmapped** |
| PRD-QRY-001 | PRD | **Query State Transitions and Constraints** | *None* | ❌ **Unmapped** |
| PRD-QRY-002 | PRD | **Query Escalation Rules** | *None* | ❌ **Unmapped** |
| PRD-QRY-003 | PRD | **Cross-Form Edit Check Execution** | *None* | ❌ **Unmapped** |
| PRD-QRY-004 | PRD | **Longitudinal Validation and Repeat-Visit Logic** | *None* | ❌ **Unmapped** |
| PRD-QRY-005 | PRD | **Field-Level SDV Flags and Audit Retention** | *None* | ❌ **Unmapped** |
| PRD-QRY-006 | PRD | **Automatic Verification Drop upon Data Modification** | *None* | ❌ **Unmapped** |
| PRD-QRY-007 | PRD | **Targeted SDV (tSDV) Sampling Algorithm** | *None* | ❌ **Unmapped** |
| PRD-SUB-001 | PRD | **State Transition Matrix & Enforcements** | *None* | ❌ **Unmapped** |
| PRD-SUB-002 | PRD | **Partial Visit Query Capability on Withdrawn Subjects** | *None* | ❌ **Unmapped** |
| PRD-SUB-003 | PRD | **Stratified Block Randomization** | *None* | ❌ **Unmapped** |
| PRD-SUB-004 | PRD | **Dynamic Minimization Algorithm** | *None* | ❌ **Unmapped** |
| PRD-SUB-005 | PRD | **Triggering and Authorizing Emergency Unblinding** | *None* | ❌ **Unmapped** |
| PRD-SUB-006 | PRD | **Immediate Unblinding State Mutation & System Actions** | *None* | ❌ **Unmapped** |
| PRD-SUB-007 | PRD | **Re-Consent Gating on Visits** | *None* | ❌ **Unmapped** |
| PRD-SYS-001 | PRD | **Standard Audit Logging (21 CFR Part 11 § 11.10(e))** | `test_prevent_audit_log_mutation` (tests/test_ledger_and_triggers.py) 🟢<br>`test_insert_generates_audit_log` (tests/test_audit.py) 🟢<br>`test_update_generates_audit_log` (tests/test_audit.py) 🟢 | ✅ **Passed** |
| PRD-SYS-002 | PRD | **Soft-Delete Enforcement and Shadow Schema Preservation** | `test_prevent_hard_delete_on_audited_model` (tests/test_ledger_and_triggers.py) 🟢<br>`test_soft_delete_generates_audit_log` (tests/test_audit.py) 🟢 | ✅ **Passed** |
| PRD-SYS-003 | PRD | **Cryptographic Ledger Hashing & Chain Validation** | `test_ledger_sealing_and_validation` (tests/test_ledger_and_triggers.py) 🟢 | ✅ **Passed** |
| PRD-SYS-004 | PRD | **Universal Site Isolation Constraint** | *None* | ❌ **Unmapped** |
| Trace-1 | SRS | **Shadow Schema Retention**<br>*Database-level hard deletes are programmatically blocked by the application layer. Deletion attempts against `AuditLog` or `AuditedModel` raise uncatchable exceptions via the SQLAlchemy listener module located in `apps/execution/database/audit.py`, ensuring a permanent shadow ledger of all system transactions.* | `test_prevent_audit_log_mutation` (tests/test_ledger_and_triggers.py) 🟢<br>`test_prevent_hard_delete_on_audited_model` (tests/test_ledger_and_triggers.py) 🟢<br>`test_hard_delete_is_prevented` (tests/test_audit.py) 🟢 | ✅ **Passed** |
| Trace-2 | SRS | **Cryptographic Key Multi-Sharing & Rotation**<br>*The system utilizes mathematical polynomial splitting (Shamir's Secret Sharing pattern) to split treatment allocation blinding keys, alongside an automatic 365-day rotation scheme for encryption keys. These operations are explicitly enforced by `AllocationKeyManager` in `apps/execution/cryptography.py`.* | `test_key_splitting` (tests/test_cryptography.py) 🟢<br>`test_encryption_decryption_with_rotation` (tests/test_cryptography.py) 🟢 | ✅ **Passed** |
| Trace-3 | SRS | **Read-Only Trial Locks & Alert Routing**<br>*Upon detecting any data compromise, the system immediately freezes clinical transactions by throwing `PermissionError` for write operations (in `audit.py`) while permitting authorized `SELECT` queries. Concurrently, high-priority notifications are dispatched to designated contacts (Email, SMS, Webhook) via the `TrialLockManager` module in `apps/execution/trial_lock.py` within one minute.* | *None* | ❌ **Unmapped** |

## 3. Unmapped Requirements

- **PRD-EDC-001** (PRD): Spreadsheet Ingestion Sheet Structure
- **PRD-EDC-002** (PRD): Field-Level Ingestion Validations
- **PRD-EDC-003** (PRD): Dynamic Skip Logic Evaluation
- **PRD-EDC-004** (PRD): Cascading Dependent Nullification (Orphan Data Safeguard)
- **PRD-EDC-005** (PRD): Real-Time Row Ingestion and Index Tracking
- **PRD-EDC-006** (PRD): Advanced Inputs (VAS and Interactive Body Maps)
- **PRD-EDC-007** (PRD): Local IndexedDB Security & State Preservation
- **PRD-EDC-008** (PRD): Conflict Resolution and Sync Reconciliation
- **PRD-EDC-009** (PRD): Visual Analog Scale (VAS) Slider Specifications
- **PRD-EDC-010** (PRD): Interactive Body Map Coordinates and Schema Mapping
- **PRD-MDR-002** (PRD): Biomedical Concept Lock State during Active Studies
- **PRD-MDR-006** (PRD): Blinding Constraints on UI Data Rendering
- **PRD-MDR-007** (PRD): Logical Mapping of I/E Criteria to eCRF Fields
- **PRD-QRY-001** (PRD): Query State Transitions and Constraints
- **PRD-QRY-002** (PRD): Query Escalation Rules
- **PRD-QRY-003** (PRD): Cross-Form Edit Check Execution
- **PRD-QRY-004** (PRD): Longitudinal Validation and Repeat-Visit Logic
- **PRD-QRY-005** (PRD): Field-Level SDV Flags and Audit Retention
- **PRD-QRY-006** (PRD): Automatic Verification Drop upon Data Modification
- **PRD-QRY-007** (PRD): Targeted SDV (tSDV) Sampling Algorithm
- **PRD-SUB-001** (PRD): State Transition Matrix & Enforcements
- **PRD-SUB-002** (PRD): Partial Visit Query Capability on Withdrawn Subjects
- **PRD-SUB-003** (PRD): Stratified Block Randomization
- **PRD-SUB-004** (PRD): Dynamic Minimization Algorithm
- **PRD-SUB-005** (PRD): Triggering and Authorizing Emergency Unblinding
- **PRD-SUB-006** (PRD): Immediate Unblinding State Mutation & System Actions
- **PRD-SUB-007** (PRD): Re-Consent Gating on Visits
- **PRD-SYS-004** (PRD): Universal Site Isolation Constraint
- **Trace-3** (SRS): Read-Only Trial Locks & Alert Routing
