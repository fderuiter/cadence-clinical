# Data Lifecycle Specification: eTMF Quality Control (QC) Review Lifecycle

## 1. Overview
The electronic Trial Master File (eTMF) Quality Control (QC) Review Lifecycle is a critical, multi-stage data review workflow implemented to guarantee data integrity, completeness, and regulatory compliance under FDA 21 CFR Part 11, GAMP 5, and EU Annex 11.

---

## 2. Document Status Values
Documents in the eTMF progress through the following status values:
- **DRAFT**: The initial, unverified state of a newly ingested or uploaded document.
- **TECHNICAL_QC**: The document is undergoing technical Quality Control checking (e.g., verifying readability, taxonomy mappings, file format compliance, and basic metadata accuracy).
- **CLINICAL_QC**: The document is undergoing clinical Quality Control review to confirm context validity, protocol alignment, and adherence to GCP/ICH standards.
- **APPROVED**: The document has successfully completed all QC phases and is officially approved as an active record in the eTMF.
- **ARCHIVED**: Active clinical records are securely archived once a study milestone or the entire trial reaches completion. This is a terminal state.
- **REJECTED**: A document that fails technical or clinical review is rejected, allowing authors to correct and resubmit it (transitioning back to DRAFT).

---

## 3. Allowed Transitions (Validated State Machine)
To prevent unauthorized state jumps or bypass of QC controls, transitions are strictly governed by a state machine validation gate:

```
[ DRAFT ] ──► [ TECHNICAL_QC ] ──► [ CLINICAL_QC ] ──► [ APPROVED ] ──► [ ARCHIVED ]
                   │                     │                    │
                   ▼                     ▼                    ▼
             [ REJECTED ]          [ REJECTED ]         [ REJECTED ]
                   │
                   ▼
               [ DRAFT ] (Re-submit)
```

- **DRAFT** can only transition to **TECHNICAL_QC**.
- **TECHNICAL_QC** can transition to **CLINICAL_QC** or **REJECTED**.
- **CLINICAL_QC** can transition to **APPROVED** or **REJECTED**.
- **APPROVED** can transition to **ARCHIVED** or **REJECTED**.
- **REJECTED** can transition to **DRAFT** (restarting the review lifecycle).
- **ARCHIVED** is a terminal state; no further transitions are permitted.

---

## 4. Role-Based Access Control (RBAC) Gates
Transitions can only be performed by users holding the designated roles:

| Target Status | Allowed Actor Roles | Description |
| :--- | :--- | :--- |
| **DRAFT** | `sponsor_dm`, `sponsor_clinical`, `admin` | Resubmitting a corrected document or reverting from rejected. |
| **TECHNICAL_QC** | `sponsor_dm`, `admin` | Technical QC review performed by Sponsor Data Managers. |
| **CLINICAL_QC** | `sponsor_clinical`, `admin`, `monitor` | Clinical QC review performed by Clinical Reviewers/Monitors. |
| **APPROVED** | `sponsor_dm`, `sponsor_clinical`, `admin` | Final validation of both technical and clinical verification steps. |
| **ARCHIVED** | `sponsor_dm`, `admin` | Relocating approved active documents to clinical archives. |
| **REJECTED** | `sponsor_dm`, `sponsor_clinical`, `admin` | Rejecting a document from any of the active QC/Approval stages. |

---

## 5. Audit Trail & 21 CFR Part 11 Compliance
Every transition executes under strict electronic signature and auditing controls:
1. **Append-Only History Logs (`DocumentQCTransition`)**: Every successful status transition is persisted in an immutable, append-only ledger tracking:
   - Document ID reference.
   - From status & To status.
   - Actor identity & Actor roles.
   - 21 CFR Part 11 change justification reason (mandatory, minimum 10 characters).
   - Timestamp.
2. **Immutable Audit Trail (`TMFAuditLog`)**: The system automatically registers a parallel record in the global eTMF audit log.
