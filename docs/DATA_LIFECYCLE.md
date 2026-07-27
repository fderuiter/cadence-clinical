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

---

# Data Lifecycle Specification: Medical Coding Lifecycle

## 1. Overview
The Medical Coding Engine translates raw, unstructured clinical verbatim descriptions (e.g., adverse events, medical history, or concomitant medications) into standard codes from dictionaries like MedDRA and WHODrug. This workflow supports precise analysis, clinical safety reporting, and submission-ready database generation while enforcing strict regulatory auditing compliance under FDA 21 CFR Part 11.

---

## 2. Ingest → Match → Assignment → Query → Recoding Flow

```
[ raw verbatim ingest ] ────► [ fuzzy matching & scoring ]
                                     │
      ┌──────────────────────────────┼──────────────────────────────┐
      ▼ (Score >= 0.85)              ▼ (Score 0.60 to 0.84)         ▼ (Score < 0.60)
[ AUTO_CODED ]               [ SUGGESTED ]                  [ QUERY_PENDING ]
      │                              │                              │
      │ (auto-promoted)              ▼ (Manual review loop)         ▼ (Triggers EDC Query)
      │                      [ ACCEPT ] or [ OVERRIDE ] ──► [ SYSTEM_CODING query ]
      │                              │                              │
      ▼                              ▼                              ▼ (Resolved by re-verbatim)
[ Active Assignment ] ◄──────────────┴──────────────────────────────┘
      │
      ▼ (Up-versioning dictionary impact)
[ ClinicalCodingLedger ] (Audit historical trail & status transitions)
```

### Stage 1: Ingest (Dictionary Loading)
- **Action**: Standard dictionaries are imported dynamically via the authenticated Gateway.
- **Accountable Roles**: `TERMINOLOGY_MANAGER` and `SYSTEM_ADMIN` hold exclusive privileges.
- **Process**: Parsing handles ASCII files inside `.zip` archives containing either MedDRA format files or WHODrug format files.
- **Auditing**: Records an immutable `DictionaryImportJob` tracking the job state, percentage completion, row counts, errors, and standard audit log entries for full lifecycle traceability.

### Stage 2: Match (Fuzzy Similarity Matching)
- **Process**: Raw text ingested into the system is normalized via:
  - Case folding.
  - Suffix-stripping stemming rules (e.g., stripping `s`, `es`, `ing`, `ed`, `ly`, etc.).
  - Clinical stop-phrase/word removal (e.g., removing words like `acute`, `mild`, `severe`, `history of`, etc.).
- **Scoring**: A hybrid deterministic scorer calculates token alignment:
  $$\text{Score} = 0.4 \times S_{\text{Levenshtein}} + 0.6 \times S_{\text{Cosine}}$$
- **Confidence Gates**:
  1. **AUTO-CODED (Score $\ge$ 0.85)**: High-confidence exact/near-exact matches are linked to standard codes immediately.
  2. **SUGGESTIONS (Score 0.60 to 0.84)**: Up to three ranked code suggestions are stored on the assignment. Status changes to `SUGGESTED`.
  3. **UNCODABLE (Score $<$ 0.60)**: Verbatim strings with low matcher confidence are placed into `QUERY_PENDING`.

### Stage 3: Assignment / Review (Manual Coder Loop)
- **Process**: Data Managers and Coders review and resolve `SUGGESTED` or `QUERY_PENDING` records.
- **Allowed Actions**:
  - **ACCEPT**: Commits suggestion index as final coding meaning.
  - **OVERRIDE**: Manually overrides the code with a verified dictionary concept.
- **Auditing / Part 11**: Coder actions require standard Gateway Signature Version 2 authentication headers, validating credentials and carrying an explicit, non-empty GxP `reason_for_change` justification. Each manual decision is recorded permanently in `ClinicalCodingLedger`.

### Stage 4: Query (Uncodable Query Generation)
- **Process**: For any `UNCODABLE` assignment, the system automatically triggers an open, actionable `ClinicalQuery` with origin `SYSTEM_CODING` and action required `RE-ENTER_VERBATIM`.
- **Identity & PII Isolation**: Query logs specify the exact table, field, and observation coordinates, but omit clinical subject IDs and demographics to maintain blinding and isolate PII data.
- **Resolution Transitions**:
  - Resolving or cancelling the `SYSTEM_CODING` query reverts the assignment status back to `UNCODED`, placing it back into the manual review loop.
  - Conversely, a manual coder action (`ACCEPT` or `OVERRIDE`) on the pending assignment automatically transitions the query status to `CLOSED` and attaches resolution notes.

### Stage 5: Recoding & Up-Versioning Ledger
- **Process**: When a new dictionary version is imported, an impact analysis compares existing coded records.
- **Outcomes**:
  - **Unchanged**: Automatic promotion to the new version.
  - **Reclassified**: Status changed to `RECODING_REQUIRED` with recoding status `PENDING`. Target is flagged for review.
  - **Deprecated**: Assigned code is no longer present; status changes to `RECODING_REQUIRED` and recoding status `PENDING` to trigger manual recoding.
- **Auditing**: Pre-upversioning historical meanings are preserved intact under the original version indices, and transitions are written idempotently to the `ClinicalCodingLedger` to maintain compliance.

---

## 3. Accountable Roles & Access Control Matrix

| Workflow Transition | Required Roles / Privileges | GxP / Part 11 Constraints |
| :--- | :--- | :--- |
| **Dictionary Ingestion** | `TERMINOLOGY_MANAGER`, `SYSTEM_ADMIN` | Synchronous layout verification & transaction rollback on failure. |
| **Fuzzy Matching** | System | Automated, deterministic logic. |
| **Accept Suggestions** | `sponsor_dm` (Data Manager), Coder synonyms | Requires gateway signature validation. |
| **Manual Override** | `sponsor_dm` (Data Manager), Coder synonyms | Requires signature validation + non-empty `reason_for_change`. |
| **Query Closure** | Coder Action or System Event | Automatically synced on manual coder decision. |
| **Impact Analysis** | `TERMINOLOGY_MANAGER`, `SYSTEM_ADMIN` | Idempotent ledger updates. |

---

## 4. Operational & Cache Configuration
- **Cache TTL**: The thread-safe medical coding lookup cache uses a configurable expiration parameter via the `CODING_CACHE_TTL` environment variable (default: `10.0` seconds).
- **Graceful Cache Fallback**: In the event of backend database errors or connectivity failures, the system automatically falls back to serving stale/expired cached results to prevent user interface degradation.
- **Supported Dictionary Formats**:
  - **MedDRA**: Stream parses standard ASCII files including `llt.asc`, `pt.asc`, `hlt.asc`, `hlgt.asc`, `soc.asc`, and `mdhier.asc`.
  - **WHODrug**: Fixed-width format files (e.g., standard B3 DD.txt, ING.txt, ATC.txt, DADA.txt, DI.txt) or delimited (CSV, PSV) text formats using custom mappings and header configurations.
- **Licensed Content Note**: In-repo storage of licensed MedDRA or WHODrug terminology distributions is strictly forbidden. All testing environments must run using small, synthetic, in-memory fixtures.
