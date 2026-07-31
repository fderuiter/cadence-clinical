# Data Lifecycle Specification

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

## EDC-to-SDTM Data Lifecycle

### Overview
This section defines the operational lifecycle for extracting clinical trial data captured in the EDC runtime into CDISC SDTM and ADaM Dataset-JSON standards for biostatistical analysis and regulatory submission.

### Lifecycle Pipeline Flow
1. **Live Data Entry**: Subject clinical data captured via eCRF screens with real-time CDASH edit checks (`edit_checks.py`).
2. **SDTM Extraction**: Clinical data extracted and mapped into core SDTM domains (`DM`, `AE`, `VS`, `LB`, `MH`) using `biostat/extractors.py`.
3. **ADaM Derivation**: Analysis datasets (`ADSL`, `ADAE`, `ADVS`) derived using `biostat/adsl.py`, `adae.py`, and `advs.py`.
4. **Dataset-JSON Serialization**: Datasets serialized into CDISC Dataset-JSON 1.0.0 format (`biostat/serializer.py`).
5. **Structural & Referential Validation**: Pre-export validation gates executed via `biostat/validator.py`.
6. **Audited Export**: Authorized export execution recorded in `BiostatExport` audit logs.

```text
[ EDC Data Capture ] ──► [ CDASH Edit Checks ] ──► [ SDTM/ADaM Extraction ]
                                                            │
                                                            ▼
[ BiostatExport Audit Log ] ◄── [ Validation Gate ] ◄── [ Dataset-JSON Serializer ]

### Access Control (RBAC)
Exports are protected by `GatewayAuthMiddleware` and `require_roles`. Authorized roles include:
* `Data Manager`
* `CRA`
* `Sponsor Statistician`
* `Statistician`

### Privacy & PII Boundary
Raw patient demographics are encrypted at rest (`ClinicalSubject.encrypted_demographics`). Subject identifiers in exports are pseudonymous (`subject_id`/`USUBJID`). Standard CDISC variables (such as `BRTHDTC`) are included in accordance with CDISC domain specifications.
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

---

# Data Lifecycle Specification: eTMF Document Redaction Lifecycle

## 1. Overview
The eTMF Document Redaction Lifecycle defines the security boundaries, operational flows, and regulatory data-handling logic for removing Personally Identifiable Information (PII) and Protected Health Information (PHI) from clinical documents before external distribution, auditor review, or public disclosure. This lifecycle fulfills the traceability and verification requirements of **PRD-TMF-005** and **Trace-12**.

---

## 2. Redaction Architecture & System Boundaries

```mermaid
graph TD
    A[Raw Unredacted Document] -->|Retained for GxP Trace Auditing| B[(Secure eTMF Storage)]
    A -->|POST /auto-redact or /manual-redact| C[De-identification Engine]
    C -->|Regex Scanners & Literal Terms| D[PII/PHI Detection & Overlap Resolution]
    D -->|Apply Transforms| E[Redacted Successor Version]
    D -->|Build Manifest| F[Redaction Manifest]
    F -->|HMAC-SHA256 Signature| G[Signed Cryptographic Manifest]
    E -->|Linked back to Source| H[(Secure eTMF Storage)]
    G --> I[TMFAuditLog REDACT Entry]

    style A fill:#fdd,stroke:#f66,stroke-width:2px
    style E fill:#dfd,stroke:#6b6,stroke-width:2px
    style B fill:#eef,stroke:#99b,stroke-width:2px
    style H fill:#eef,stroke:#99b,stroke-width:2px
```

The redaction engine is split into two layers:
1. **Shared Detection Layer (`packages/deid`)**: A pure-Python detection and sanitization package implementing regex-based scans, literal word scans, overlap resolution, transformation strategy application, and cryptographic signature generation.
2. **Service Gateway Layer (`apps/etmf`)**: Exposes `/api/v1/etmf/documents/{document_id}/auto-redact` and `/manual-redact` endpoints. It resolves versions, validates and logs Part 11 justifications, writes non-sensitive audit events, and restricts access to raw unredacted original files.

---

## 3. Compliance Profiles & Regulatory Disclosure Contexts

The de-identification engine implements three discrete, standardized compliance profiles that govern active PII/PHI categories and operational intents:

### HIPAA (US Health Insurance Portability and Accountability Act)
- **Operational Intent**: Satisfies the US "Safe Harbor" de-identification standard for sanitizing documents to be shared with sponsors, research partners, or US regulatory agencies (FDA).
- **Active Categories**: Direct and indirect identifiers (Emails, Phone/Fax Numbers, Social Security / National IDs, IP/MAC Addresses, URLs, ZIP/Geographic codes, Dates, Medical Record/Account Numbers, Age above 89, and custom terms).

### GDPR (EU General Data Protection Regulation)
- **Operational Intent**: Satisfies strict personal data handling rules for clinical subjects and trial coordinators residing in the EU. Focuses on removing direct and indirect identifiers that could lead to identity reconstruction.
- **Active Categories**: Direct and indirect identifiers (Emails, Phone/Fax Numbers, Social Security / National IDs, IP/MAC Addresses, URLs, ZIP/Geographic codes, Dates, Medical Record/Account Numbers, Age above 89, and custom terms).

### EU CTR (European Union Clinical Trials Regulation)
- **Operational Intent**: Focuses on the public-disclosure framing mandated by the EU Clinical Trials Registry (under Regulation EU No 536/2014). Ensures clinical study documents can be published transparently to the public database without revealing any patient identities, while maintaining geographic granularity (ZIP codes and IP addresses) which are relevant to clinical execution and are thus preserved.
- **Active Categories**: Focuses strictly on patient anonymity and direct clinical trial patient identifiers (Emails, Phone/Fax Numbers, Social Security / National IDs, Dates, Medical Record/Account Numbers, Age above 89, and custom terms).

---

## 4. De-identification Transforms & Default Date-Shifting

The engine applies distinct, GxP-compliant transform strategies to the detected matches:
1. **Masking (`mask`)**: Replaces the sensitive value with a standard placeholder (e.g., `[EMAIL]`, `[SSN_NATIONAL_ID]`).
2. **Deterministic Pseudonymization (`pseudonymize`)**: Generates a cryptographically strong, non-reversible, deterministic hash of the verbatim value using HMAC-SHA256 and the workspace `REDACTION_SIGNING_SECRET` / `"internal-gateway-secret-12345"`.
3. **Age Capping (`age_cap`)**: Generalizes age values that exceed a set limit. Standard policy generalizes any age above 89 to `89+`.
4. **Configurable Date-Shifting (`date_shift`)**:
   - **Default Date-Shift Policy**: By default, dates are shifted forward by exactly **365 days** (1 year) to preserve longitudinal intervals (e.g., matching subsequent visits, adverse event spans, or dosing times) while destroying original calendar values.
   - **Configurability**: The date shifting offset is fully configurable via the `shift_days` parameter on the transform execution to handle study-specific anonymization schedules.

---

## 5. Document Version Preservation & Access Boundaries

To satisfy 21 CFR Part 11 electronic records tracing and GxP compliance:
- **Non-Destructive Version Preservation**: Original, unredacted documents are never overwritten. A redaction event increments the document's `version_index` and creates a redacted successor document version linked back to the source version using the `redaction_source_id` reference column.
- **Auditor & Inspector Lock state**: Read-only roles (`auditor`, `inspector`, `regulatory_inspector`) are strictly blocked from accessing the raw, unredacted source documents (returning HTTP 403 Forbidden) once a redacted successor exists. Only write-privileged roles (e.g., Sponsor DM) can view raw originals.
- **Trial Lock Safeguards**: If the clinical study or trial is locked, any subsequent ingestion, manual/automated redaction, or transition attempts are blocked, returning HTTP 403 `IMMUTABILITY_VIOLATION`.

---

## 6. Manifest Signing, Audit Trails & Sensitive Data Restrictions

Every redaction operation creates a highly detailed, immutable cryptographic paper trail:
1. **Signed Redaction Manifest**:
   - A structured Pydantic-based `RedactionManifest` records redaction counts per category, operator identity, change reason justification, source version, target version, and character span metadata.
   - It is signed symmetrically using HMAC-SHA256 with the secret `REDACTION_SIGNING_SECRET`.
   - The signed manifest data is saved permanently inside the redacted document's `redaction_manifest_json` column.
2. **Sensitive-Data Restrictions (PII/PHI Exclusion)**:
   - To maintain blinding and comply with GDPR/HIPAA standards, **raw matched PII/PHI values are strictly excluded from all audit trails, logging records, and manifest files**. Only the category, strategy, and replacement values are preserved.
3. **Immutable Audit Trail Logging**:
   - The system logs a non-sensitive `REDACT` action to the immutable database-backed `TMFAuditLog`, containing the actor ID, roles, source/redacted version indices, and the cryptographic manifest signature to ensure tamper-evident non-repudiation.

---

# Data Lifecycle Specification: Global Library & Clinical Study Instances

## 1. Overview
The Global Library in the Metadata Designer (MDR/SDR) service (`apps/designer`) serves as the central, multi-tenant repository for reusable clinical protocol definitions. This specification defines the data lifecycle, retention rules, and strict tenant partitioning that separate shared global reference templates from trial-specific (study instance) execution data. It ensures system compliance under FDA 21 CFR Part 11, GxP standards, and GDPR multi-tenant guidelines, satisfying **Trace-3**.

---

## 2. Shared Library Objects versus Study-Instance Data

The platform enforces a clear distinction between master template objects and localized trial instances:

```
[ Global Library (Master Data) ] ──────► [ POST /library-instances ] ──────► [ Study Instance (Execution) ]
 - Owned by Sponsor A                     - Copy-on-Instantiation            - Scope bound to Study
 - Versioned via Graph Chains             - Captures source link             - Local Overrides Allowed
 - Locked statuses are Immutable          - Retains pedigree trace           - Lifespan bound to Trial
```

### Global Library Templates (Master Reference Data)
- **Nature**: High-quality, reusable blueprint templates representing clinical design standards (`FORM`, `DATA_ELEMENT`, `ARM`, `VISIT`).
- **Storage**: Persisted as graph nodes inside Neo4j.
- **Auditing & Change Trails**: Modifications create new versioned nodes. Prior states are retained intact and chained linearly using `[:PREVIOUS_VERSION]` relationships to preserve historical protocol reproducibility.

### Study Library Instances (Trial-Specific Data)
- **Nature**: Active, study-scoped configurations instantiated for a particular clinical protocol.
- **Storage**: Persisted as separate `:LibraryObjectInstance` nodes linked to the study root `:Study`.
- **Overrides**: Study teams can customize or override these instantiated templates.
- **Source Linkage**: Upon instantiation, the platform records a strict `[:INSTANTIATED_FROM]` relationship mapping the instance back to the exact source library template version (tracking source ID, version index, and sponsor ID). This guarantees absolute provenance and clinical traceability.
- **Isolation of Modifying Effects**: Local overrides exist purely at the study-instance level. Modifying an instantiated copy has absolutely zero impact on the master Global Library template, preserving the template's purity.

---

## 3. Logical Tenant Partitioning & Sponsor Separation Guidelines

To enforce strict clinical trial separation and prevent cross-sponsor metadata leakage:
1. **Cryptographic Context Verification**: The API Gateway decodes the caller's Keycloak JWT, validates roles, and injects signed headers (`X-Sponsor-Id`, `X-Tenant-Id`) downstream.
2. **Whitespace Gating**: The Metadata Designer service strictly parses incoming sponsor attributes. Write, read, list, or transition attempts are instantly rejected with HTTP 403 Forbidden if the sponsor ID is:
   - Absent or missing.
   - Null or empty (`""`).
   - Whitespace-only (e.g., `"   "`).
3. **Sponsor Boundary Enforcement**: Database queries are strictly scoped. Every query automatically appends a sponsor isolation parameter (e.g. `n.sponsor_id = $sponsor_id`). Callers are completely blocked from reading, listing, updating, or instantiating templates belonging to other sponsors (returning HTTP 404 or 403).

---

## 4. Retention Policy & Lifespan Rules

The operational lifespan of library data and study data is governed by distinct regulatory retention schedules:

| Data Classification | Lifecycle States | Retention Trigger | Compliance Retention Timeline |
| :--- | :--- | :--- | :--- |
| **Global Library Templates** | `DRAFT`, `IN_REVIEW`, `APPROVED`, `PUBLISHED`, `ARCHIVED` | Transition to `ARCHIVED` | Permanently retained as master metadata. Retained for 25 years post-trial completion per clinical master file guidelines. |
| **Study Library Instances** | Active Trial State | Trial Completion or Soft Deletion | Linked directly to the study lifecycle. Retained/archived in parallel with study trial master records. |

### Immutability of Locked Template Statuses
- Once a template version's status is transitioned to `PUBLISHED` or `ARCHIVED`, its payload is locked. Standard `PUT` mutations on these records are strictly blocked at the API layer, raising an `IMMUTABILITY_VIOLATION` (HTTP 403 Forbidden).
- **Formal Amendments**: To evolve a locked or in-use template, users must call `/api/v1/mdr/library/{id}/amend`. This copies the template's payload into a new, separate draft version node (incrementing the version sequence) while keeping existing active studies linked to the original version.

### Non-Destructive Soft-Deletion Guidelines
- Deletions are strictly non-destructive. To prevent historical audit trail breaks, master templates and study instances are never deleted from the database. Instead:
  - Deletions write a new version marked as `is_deleted = true`.
  - The previous active state remains securely preserved in the graph version chain, enabling complete retrospective reconstructibility of any trial configuration at any historical timestamp.

---

# Data Lifecycle Specification: Protocol Amendment Lifecycle

## 1. Overview
The Protocol Amendment and Clinical Data Lifecycle governs mid-study protocol modifications, version propagation, historical immutability, and patient safety re-consent gating. To safeguard clinical study integrity under FDA 21 CFR Part 11, GAMP 5, and EU Annex 11, the system guarantees that historical records are never overwritten (zero data loss) and that active clinical transitions require explicit, documented patient consent corresponding to the approved protocol version tag. This section traces back to the requirements of **PRD-SYS-001**, **PRD-MDR-002**, **PRD-SUB-007**, **TDD §3.4/§3.5**, and **QA §5.1 TC-VAL-LOG-001**.

---

## 2. Protocol Version Statuses
Protocol versions progress through a validated state machine, representing controlled stages of clinical approval:

- **DRAFT**: The initial, mutable state of a new or amended study protocol. All graph elements (arms, epochs, visits, forms, blocks) can be updated or deleted.
- **ACTIVE**: The protocol version is deployed but not yet finalized or released to clinical production.
- **LOCKED**: The version is frozen and undergoes final signature validation checks. All modifications are strictly blocked.
- **PUBLISHED**: The version is formally released to clinical execution sites. This is a GxP-validated master metadata record.
- **ARCHIVED**: Superseded or retired versions are archived for regulatory audit history. They remain permanently readable.
- **FROZEN**: A transient state indicating a version has been finalized and cannot be modified under any standard workflow.

### Mutable vs. Immutable Lifecycle States
- **Mutable States**: `DRAFT` and `ACTIVE`. Graph elements can be added, updated, or soft-deleted.
- **Immutable/Frozen States**: `LOCKED`, `PUBLISHED`, `ARCHIVED`, and `FROZEN`. Standard PUT/POST/DELETE operations instantly raise an immutability violation error.

---

## 3. Amendment Branching and Version Succession Flow
When a study designer initiates an amendment on a finalized protocol version, the system creates a transaction-safe branch (cloned subgraph) without altering the source version:

```mermaid
graph TD
    Parent[Parent Version: LOCKED/PUBLISHED] -->|POST /api/designer/protocols/{id}/amend| Amend[Amend Engine]
    Amend -->|Deep-Copy Subgraph| Successor[New Version: DRAFT]
    Successor -->|PREVIOUS_VERSION Link| Parent
    Successor -->|version_index increment| IncIndex[version_index = parent + 1]
    Successor -->|version_tag bump| BumpTag[Tag: v1.0 -> v1.1 or v2.0]
```

- **Branching Action**: A formal amendment fork executes deep copies of up to 4 levels of structural relations (HAS_ARM, HAS_EPOCH, HAS_VISIT, HAS_FORM, HAS_ACTIVITY).
- **History Linkage**: The new DRAFT successor is connected back to its immediate parent node via a `[:PREVIOUS_VERSION]` relationship, preserving an unbroken pedigree chain for full retrospective protocol reconstruction.

---

## 4. Designer Service Mechanics and Immutability Guards
The Metadata Designer service (`apps/designer`) enforces GxP metadata integrity through structural and API-level constraints:

### API Endpoints and Contract Shapes for Versioning
The following endpoints orchestrate the metadata versioning lifecycle:
- **Version Creation**: `POST /api/v1/studies/{study_id}/versions` initiates a new study version. It receives a `CreateStudyVersionRequest` payload containing properties `id`, `version_tag`, `status`, and `version_index`, returning a standard status confirmation.
- **Protocol Amendment**: `POST /api/designer/protocols/{id}/amend` deep-copies the entire parent protocol configuration. It receives a `ProtocolAmendRequest` payload containing fields `amendment_type` (defaulting to `"minor"`) or `type`, returning a structured response containing `{new_version, status, parent_version}` to verify successor generation.
- **Form-Level Graph Diff**: `GET /api/v1/studies/{study_id}/versions/diff` compares two subgraphs, returning `added_nodes`, `modified_nodes`, and `deleted_nodes` based on key and XML comparisons.
- **Field-Level Diff**: `GET /api/v1/studies/{study_id}/differences` executes a 1D in-memory flat difference mapping of flattened dot-notated paths.

### Immutability Guards and Branching semantics
- **Assertion Handlers**: `assert_study_version_mutable` and `assert_graph_mutable` run on every mutation, raising a `403 Forbidden` exception if the target's status resides in `APPROVED`, `SIGNED`, `LOCKED`, `PUBLISHED`, or `ARCHIVED`.
- **Version Bumping Rules**: Bumping a version (`bump_version`) performs a major bump (e.g. `1.0` $\rightarrow$ `2.0`) for `"major"` or `"restructuring"` types, and a minor bump (e.g. `1.0` $\rightarrow$ `1.1`) otherwise. The `version_index` always increments by exactly `1` (verified in `tests/test_study_versions.py`).
- **Cryptographic Version Integrity**: To prevent out-of-band tampering, study version attributes are checked using a canonical HMAC-SHA256 signature generated and verified symmetrically (`generate_canonical_signature`/`verify_version_signature` in `packages/security/signing.py`). This is strictly used for cryptographic integrity verification of study payloads before loading, and is independent of user electronic signatures.
- **Non-Destructive/Immutable-History Guarantee**: To preserve clinical audit trails, study metadata configurations are never physically deleted. Soft deletions write a new version marked as `is_deleted = true`, keeping historical structures intact.

---

## 5. Error & Concurrency Contracts
The Metadata Designer and Execution services implement a unified exception-to-HTTP mapping to ensure standard GxP error representation:

| Internal Python Exception | HTTP Code | Error Code / Details | Description |
| :--- | :--- | :--- | :--- |
| `ImmutabilityViolationError` | `403` | `IMMUTABILITY_VIOLATION` | Raised when attempting to mutate a locked, published, or archived graph or version. |
| `ConcurrentLockingError` | `409` | `CONCURRENT_LOCKING_CONFLICT` | Raised during parallel creation of identical version indexes or tags. |
| `InvalidSignatureError` | `400` | `INVALID_OR_MISSING_SIGNATURE` | Raised when a study version's cryptographic canonical signature fails verification. |
| `LibraryObjectInUseError` | `409` | `LIBRARY_OBJECT_IN_USE` | Raised when modifying a Global Library template currently in use by an active study. |

### Concurrency-Safety Model
1. **Neo4j Study Root Locking**: The Designer service executes an exclusive write-lock (`SET s._lock = true`) on the Study root node during amendments and version promotions to serialize graph updates.
2. **Sequential Version Indices Guard**: Databases enforce unique composite indexes on `(study_id, version_index)` and `(study_id, version_tag)` to prevent race conditions from creating parallel timelines.
3. **Mock/In-Memory Fallback Path**: For non-Neo4j testing environments, a thread-safe dictionary-based locking scheme (`_amendment_locks`) handles isolation in memory.

---

## 6. Accountable Roles & Access Control Matrix

| Action / Transition | Allowed Actor Roles | GxP / Part 11 Constraints |
| :--- | :--- | :--- |
| **Initiate Study version** | `sponsor_designer`, `sponsor_dm`, `admin` | Requires explicit `X-Change-Reason` header. |
| **Amend Study version** | `sponsor_designer`, `sponsor_dm`, `admin` | Spawns a transaction-safe draft copy; parent remains immutable. |
| **Lock / Publish version** | `sponsor_designer`, `sponsor_dm`, `sponsor_admin`, `admin` | Performs cryptographic canonical signature generation. |
| **Execute PI Sign-Off** | `Site Principal Investigator (PI)`, synonyms | Enforces re-authentication step-up token and records GxP signature. |
| **Record Re-Consent** | `Site Investigator`, `CRC`, `admin` | Instantly unblocks execution gating on clinical data tables. |

---

## 7. Audit Trail & 21 CFR Part 11 Compliance
Every state change, protocol version transition, and clinical transaction generates an append-only, immutable paper trail that complies with GxP 21 CFR Part 11 standards:

1. **Mandated Audit Fields (`PRD-SYS-001`)**: Every table representing metadata or transaction states inherits and enforces the presence of exactly four core audit fields:
   - `created_at`: High-precision UTC timestamp generated by the server upon commit.
   - `created_by`: Deterministic OIDC user identity string (`sub`) of the authenticated user performing the write.
   - `reason_for_change`: A mandatory string of minimum 10 characters and maximum 1000 characters capturing the clinical/business justification.
   - `version_index`: An integer representing the version sequence of the record, beginning at `1` and auto-incrementing by `1` with every update.
2. **Append-Only History Records**: Mutations do not overwrite existing transaction blocks. Changes to protocol configurations write new Action records (`Action` / `change_reason`) to the immutable history log while soft-deletes write a deleted state, maintaining full reconstructibility of any trial configuration at any historical timestamp.

---

## 8. Execution Service Re-Consent Gating
Clinical Trial Execution (`apps/execution`) enforces exact-version re-consent gating to protect patient safety.

### SubjectConsent Data Model
The `SubjectConsent` table stores subject-specific consent statuses:
- `study_id`: Alphanumeric study identifier.
- `version_tag`: Alphanumeric protocol version tag (e.g., `"2.0"`).
- `version_index`: Positive integer chronological version index.
- `icf_signed`: Boolean indicating if the Informed Consent Form is signed.
- `icf_signed_date`: UTC timestamp of the signature.
- `requires_reconsent`: Boolean indicating if this version requires subjects to sign a new consent before entering more data.

### The Session before_flush Gating Mechanics
- **The Database Gate**: Inside `apps/execution/database/audit.py`, a `before_flush` event listener intercepts modifications to clinical tables (`clinical_subjects`, `clinical_visits`, `clinical_observations`, `form_submissions`).
- **Gating Evaluation**: The database gate checks if any higher-index protocol version is flagged as `requires_reconsent = true`. If a subject has not signed a matching `SubjectConsent` record for that newer version, the gate instantly aborts the database transaction and raises:
  `PermissionError("Re-Consent Required - Demographics & Visit Forms Locked")`
- **Clearing Semantics**: Recording a new `SubjectConsent` with `icf_signed = true` corresponding to the latest version immediately clears the blocking flag, allowing clinical workflow writes to proceed (validated by `tests/test_reconsent_blocking.py` under `PRD-SUB-007`).
- **Upstream Separation**: The `apps/econsent` module functions strictly as an independent upstream translation and translation-caching service, and does not participate in execution-level transaction blocking or amendment gating.

---

## 9. Planned / Pending Implementation

### Feature #321: Protocol Version Stamping & Non-Destructive Reconciliation (Future Scope)
* **Protocol Version Stamping**: Future releases will introduce mandatory protocol version-stamping on clinical transaction entities. Every newly created `ClinicalObservation` and `FormSubmission` will store the active `protocol_version_tag` and `protocol_version_index` at the moment of entry.
* **Non-Destructive Reconciliation**: When migrating existing subject records to an amended protocol version, the system will apply non-destructive migration rules. For fields that are renamed or removed, the original historical observation entries will remain untouched. The system will write a successor observation mapping the new target coordinates, tracking provenance through a migration source ID reference to ensure no data loss occurs.

### Feature #331: eTMF Linkage and version History (Future Scope)
* **eTMF Linkage**: Future releases will connect eTMF documents directly to the protocol versions they govern. The `TMFDocument` model will establish a foreign key or graph relationship mapping to the canonical `ProtocolVersionRef`.
* **ExpectedDocument Alignment**: Seeding expected document templates (`ExpectedDocument`) will dynamically adapt according to the active protocol version. When a protocol version transitions, the expected document list will automatically register new required documents (e.g. adding a new Consent Form requirement for v2.0), while archiving outdated requirements in accordance with the GxP data preservation policy.

---

# Data Lifecycle Specification: Native 21 CFR Part 11 eSignature Lifecycle

## 1. Overview
The Native 21 CFR Part 11 eSignature Lifecycle governs the progression of clinical and regulatory artifacts from unsigned drafts to fully signed, cryptographically secured, and immutable historical records. This workflow ensures non-repudiation, signer re-authentication, and strict state locking across all core microservices, satisfying **PRD-SYS-001** and **PRD-TMF-005**.

---

## 2. Unsigned-to-Signed Transition Lifecycle

```
[ Artifact Ingestion ]
         │
         ▼
[ PENDING / UNSIGNED ] ──( Re-Authenticate & Apply Sign-off )──► [ SIGNED / APPROVED ]
   - status: active QC states                                       - status: "SIGNED"
   - approval_status: "PENDING"                                     - approval_status: "APPROVED"
   - signature_manifestation: null                                  - signature_manifestation: [Certificate-bound Block]
   - Mutations Allowed (QC transitions, redaction)                  - Mutations Blocked (IMMUTABILITY_VIOLATION)
```

1. **Active/Unsigned Phase**: Newly ingested artifacts (e.g. eTMF documents, protocol versions) begin with `approval_status = "PENDING"`. In this state, they can undergo active QC review transitions, automated/manual redactions, or minor updates.
2. **Re-Authentication Trigger**: Applying an electronic signature requires immediate "double-keying" re-authentication of the signer's credentials, regardless of an active session.
3. **Locked/Signed Phase**: Upon successful re-authentication and signature execution, the artifact's `status` transitions to `SIGNED` and its `approval_status` transitions to `APPROVED`. The record becomes completely locked, preventing all future edits.

---

## 3. Dual-Layer Security: Gateway Authorization vs. Downstream Manifestation

To secure electronic records without propagating raw credentials across service boundaries, the architecture enforces a strict dual-layer authorization-manifestation design:

### Layer 1: Gateway Signature Token Authorization (Short-Lived Intent)
- **Path**: `apps/gateway/main.py` ➔ `packages/security/middleware.py`
- **Mechanism**: The user re-enters their password (and optional TOTP) into the reusable Vue 3 component `apps/web/src/components/SignatureCaptureModal.vue`. The API Gateway validates these credentials against Keycloak and issues a short-lived **Signature Token (`X-Sig-Token`)** signed via HS256 with `GATEWAY_SECRET`.
- **Properties**:
  - **Temporal Limitation**: Hard expired in **60 seconds** (`exp = iat + 60.0`).
  - **Single-Use Replay Prevention**: Contains a unique UUID `jti` verified against an in-memory/distributed cache to block replay attacks.
  - **Action & Identity Binding**: Explicitly bound to the executing user (`sub` claim) and the exact REST endpoint route (`action` claim).

### Layer 2: Certificate-Bound Record Manifestation (Persistent Non-Repudiation)
- **Path**: `apps/etmf/main.py` or `apps/designer/main.py`
- **Mechanism**: Upon verifying the `X-Sig-Token`, the downstream service generates a transient RSA private key and self-signed X.509 certificate on-the-fly.
- **Persistent Signature Block**: The service signs the canonical representation of the record (including its SHA-256 content hash, signer OIDC ID, UTC timestamp, and controlled signing reason).
- **Result**: A structured, mathematically verifiable `SignatureManifestation` (containing the signature, certificate PEM, and key identifier) is persisted permanently in the record's database columns (`signature_manifestation` / `signature_manifestation_json`). This fulfills the Part 11 requirements for the printed name of the signer, UTC timestamp, and the meaning of the signature (§ 11.50).

---

## 4. Trust Boundaries & Token/Error Contracts

The interaction across the UI, Gateway, and Downstream microservices is governed by clear error and token contracts:

| Event Scenario | HTTP Code | Error Code / Contract | Actionable UI Mitigation |
| :--- | :--- | :--- | :--- |
| **Missing/Expired Token** | `401 Unauthorized` | `REAUTHENTICATION_REQUIRED` or `JWTExpired` | Forces user to re-verify credentials in the modal and requests a fresh `X-Sig-Token`. |
| **User/Action Mismatch** | `401 Unauthorized` | `Mismatched signature token user` / `Action mismatch` | Rejects the signature execution, preventing token hijacking or cross-endpoint routing. |
| **Insufficient RBAC Roles** | `403 Forbidden` | `ROLE_INSUFFICIENT` / Permission check failure | Modal displays an error stating the user is not authorized to sign. |
| **Post-Signature Edit Attempt** | `403 Forbidden` | `IMMUTABILITY_VIOLATION` | Returns a blocked status response, writes a `MUTATION_REJECTED` audit event, and denies the update. |

---

## 5. Architectural Map of Components

The Part 11 eSignature workflow is fully realized and integrated across the following source paths:

- **Identity & Step-up Gateway Authentication**:
  - `apps/gateway/main.py` (Verify password and issue `X-Sig-Token`)
  - `packages/security/middleware.py` (Intercept and validate token claims)
  - `docs/SDLC/Signature_Token_Cryptographic_Contract.md` (Formal JWT specification)
- **Reusable Frontend Modal**:
  - `apps/web/src/components/SignatureCaptureModal.vue` (Credential capture, auto-clear, and error mapping)
- **eTMF Service Execution**:
  - `apps/etmf/main.py` (Sign-off endpoint: `POST /api/v1/etmf/documents/{document_id}/sign-off`)
  - `apps/etmf/models.py` (Persistence schema: `TMFDocument.signature_manifestation`)
  - `tests/test_etmf_signing_lifecycle.py` (E2E signing lifecycle and Merkle seal verification)
- **Metadata Designer Execution**:
  - `apps/designer/main.py` (Protocol approval endpoint: `POST /api/v1/studies/{study_id}/versions/{version_id}/approve`)
  - `apps/designer/delta.py` (Approve study delta and lock protocol graph nodes)

---

# Data Lifecycle Specification: SDTM/ADaM Export Lifecycle & Privacy Policy

## 1. Overview & Objectives
To support regulatory clinical trial submissions (such as FDA or EMA), clinical data captured in downstream EDC/execution transaction databases must be transformed, validated, and serialized into CDISC-compliant formats. Specifically, the system extracts **SDTM** (Study Data Tabulation Model) domains, derives **ADaM** (Analysis Data Model) datasets, and bundles them into the standardized **CDISC Dataset-JSON 1.0.0** schema format.

The primary objective of the SDTM/ADaM Export Pipeline is to deliver clean, de-identified, submission-ready datasets synchronously while enforcing strict regulatory compliance, patient privacy preservation (via deterministic transformations), and robust GxP audit tracking (under **FDA 21 CFR Part 11**, **ADR-094**, and **ADR-108**).

---

## 2. End-to-End Export Lifecycle & Pipeline Flow

The export pipeline processes transactional clinical databases into CDISC Dataset-JSON payloads through the following six distinct lifecycle stages:

```
[ Downstream Transaction DB ]
             │
             ▼ (Stage 1: Extraction & Re-Consent/Protocol Reconciliation)
[ Filtered Subjects/Observations ]
             │
             ▼ (Stage 2: Mappings & Concomitant Medications (CM) Mapping)
[ Declarative Mapping & Sequencing ] ──► (Stage 3: ADaM Derivation Engine)
             │                                       │
             ▼                                       ▼
[ Assembled SDTM Records (with SUPP--) ]   [ Assembled ADaM Records ]
             │                                       │
             └───────────────────┬───────────────────┘
                                 │
                                 ▼ (Stage 4: Deterministic Privacy Transforms)
                       [ De-identified Data ]
                                 │
                                 ▼ (Stage 5: CDISC Dataset-JSON Serialization)
                     [ Raw DatasetJSON Object ]
                                 │
                                 ▼ (Stage 6: Schema Validation Gate)
                     [ Schema Validation (HTTP 422 on Fail) ]
                                 │
                                 ▼ (Stage 7: Synchronous Delivery & Audit Logging)
              [ Synchronous JSON Payload + BiostatExport Audit Row ]
```

### Stage 1: Extraction & Protocol Reconciliation
- **Data Sourcing**: The pipeline queries active, non-deleted clinical subjects (`ClinicalSubject`) and observations (`ClinicalObservation`) scoped to a specific `study_id`.
- **Protocol Reconciliation**: The system executes dynamic, non-destructive reconciliation (`reconcile_observations`) based on the subject's latest approved protocol version or consent tag to map historical observations to current structure standards before feeding the extractor.

### Stage 2: Mappings & Concomitant Medications (CM) Mapping
- **Declarative Mapping**: Extracted fields are mapped to standard variables using a declarative pipeline defined in `SDTM_MAPPINGS` (in `apps/execution/biostat/mappings.py`).
- **Concomitant Medications (CM) Mapping**: The pipeline provides comprehensive extraction and mapping coverage for the Concomitant Medications (`CM`) domain. Verbatim medication names entered by investigators are mapped and sequenced alongside crucial variables:
  - `CMSEQ`: Monotonically increasing sequence integer per subject, sorted by medication start date (`CMSTDTC`).
  - `CMTRT`: Reported verbatim name of the medication.
  - `CMDECOD`: Standardized medication name (WHODrug Preferred Name) enriched via export-time terminology lookup.
  - `CMCLAS`: Medication Class (WHODrug Drug Class).
  - `CMDOSE` / `CMDOSEU` / `CMDOSFRQ` / `CMROUTE`: Medication dose, units, frequency, and route of administration.
  - `CMSTDTC` / `CMENDTC`: Start and end dates in ISO 8601 format.

### Stage 3: ADaM Derivation Engine
- **ADaM Datasets**: Utilizing extracted SDTM domains, the derivation engine dynamically computes Subject-Level Analysis (`ADSL`), Adverse Events Analysis (`ADAE`), and Vital Signs Analysis (`ADVS`) datasets.
- **Complex Derivations**: ADaM-specific algorithms derive complex parameters such as treatment emergence (`TRTEMFL`), change from baseline (`CHG`, `PCHG`), relative analysis days (`ASTDY`, `AENDY`), and analysis visit numbers (`AVISITN`).

### Stage 4: Deterministic Privacy Transformations
- Assembled SDTM/ADaM records are run through a secure, deterministic de-identification pass (`deidentify_export_data`) immediately prior to serialization. This ensures raw patient identifiers (PII/PHI) and true chronological dates are redacted, preserving privacy while maintaining complete referential and longitudinal consistency (see **Section 5** below for detail).

### Stage 5: CDISC Dataset-JSON Serialization
- Extracted domain/dataset records are dynamically mapped to Pydantic v2 CDISC Dataset-JSON domain models (`apps/execution/biostat/models.py`), structuring metadata, variable attributes (types, labels, formats), and record data matrices according to the **CDISC Dataset-JSON 1.0.0** specification.

### Stage 6: Schema Validation Gate
- Every generated `DatasetJSON` instance is fed to `validate_dataset_json()`. If any records violate schema parameters (e.g. missing keys, empty `STUDYID`, incorrect value types, or broken structural variables), a `DatasetJSONValidationError` is raised, triggering an automatic transactional rollback. The API aborts the request and returns an **HTTP 422 Unprocessable Entity** error.

### Stage 7: Synchronous Delivery & Audit Logging
- **Synchronous Responses**: Verified Dataset-JSON payloads are returned immediately in the HTTP response body with media type `application/json`.
- **Immutable Audit Logging**: Every export attempt (success or failure) is logged synchronously inside the same database transaction to the immutable `BiostatExport` table.
  - **Success Row**: Records the study identifier, export type (`SDTM`, `ADaM`, or `BUNDLE`), target dataset/domain name, and `status = "SUCCESS"`.
  - **Failure Row**: Records the metadata, `status = "FAILED"`, and the detailed, GxP-scrubbed exception message in the `error_message` column to ensure robust inspection-ready audit trails without leaking PHI.

---

## 3. Exact API Contract Specification

The biostatistical export pipeline is exposed through three secure, authenticated endpoints under the central API Gateway:

### 1. Export SDTM Domain
- **Endpoint**: `GET /api/v1/execution/biostat/sdtm/{domain}`
- **Path Parameter**: `domain` - One of `DM`, `AE`, `VS`, `LB`, `MH`, `CM`.
- **Query Parameter**: `study_id` (string, Required) - The unique study identifier.
- **Supplemental Contract**: If matching supplemental qualifier records exist for the requested domain (e.g., custom attributes not mapped to standard SDTM variables), a parallel **`SUPP<domain>`** dataset (e.g. `SUPPAE`, `SUPPVS`, `SUPPLB`, `SUPPMH`, `SUPPCM`) is dynamically generated and appended alongside the parent dataset within the same Dataset-JSON response.

### 2. Export ADaM Dataset
- **Endpoint**: `GET /api/v1/execution/biostat/adam/{dataset}`
- **Path Parameter**: `dataset` - One of `ADSL`, `ADAE`, `ADVS`.
- **Query Parameter**: `study_id` (string, Required) - The unique study identifier.

### 3. Export Biostatistical Bundle
- **Endpoint**: `GET /api/v1/execution/biostat/bundle`
- **Query Parameter**: `study_id` (string, Required) - The unique study identifier.
- **Bundle Aggregation Behavior**: Compiles all supported SDTM domains (`DM`, `AE`, `VS`, `LB`, `MH`, `CM`), their respective supplemental qualifier datasets (`SUPPDM`, `SUPPAE`, `SUPPVS`, `SUPPLB`, `SUPPMH`, `SUPPCM`), and all derived ADaM datasets (`ADSL`, `ADAE`, `ADVS`) in a single consolidated CDISC Dataset-JSON 1.0.0 payload. Returns HTTP 404 if no records are found for the study.

### Authorization Roles (RBAC Gates)
All export endpoints are protected by the `GatewayAuthMiddleware` and require the caller to hold one of the following authorized clinical or administrative roles:
- `ROLE_CRA` (Clinical Research Associate)
- `ROLE_DATA_MANAGER` (Data Manager / Sponsor Data Manager)
- `sponsor_statistician` / `statistician` (Clinical Statisticians)

### HTTP Error Mapping Contract
The pipeline enforces standard GxP exception handling and HTTP response codes:

| HTTP Status Code | Reason Code / Error Detail | Description |
| :--- | :--- | :--- |
| **400 Bad Request** | `Unsupported SDTM domain` / `Unsupported ADaM dataset` | Raised when the requested domain or dataset is not supported. |
| **401 Unauthorized**| `Missing gateway authentication headers` | Raised when gateway-signed headers or credentials are absent. |
| **403 Forbidden**   | `Role check failure` / `Insufficient permissions` | Raised when the caller does not hold an authorized biostatistical role. |
| **404 Not Found**   | `No biostat records found for the given study.` | Returned by the bundle endpoint when no data is captured for the study. |
| **422 Unprocessable**| `Dataset-JSON validation failed: <message>` | Raised when the generated payload violates Dataset-JSON schemas or study contracts. |
| **500 Internal Error**| `Export execution failed: <message>` | Catch-all for downstream execution errors. Logs a FAILED audit row. |

---

## 4. Response-Data-Quality Boundary & Reference Issues

The biostatistical pipeline defines a strict data-quality and propagation boundary to isolate development responsibility and guarantee submission purity:

- **Supplemental Qualifiers Propagation (#719)**: The propagation of existing, extractor-produced supplemental qualifiers (`SUPPAE`, `SUPPVS`, `SUPPLB`, `SUPPMH`, `SUPPCM` records) is owned strictly under **#719**. This guarantees that all custom form entries, unmapped eCRF observations, and parent-identifying structures are correctly structured into parallel `SUPP--` itemGroupData blocks alongside their standard parent records.
- **Null-Flavor Emission & Coding-Assignment Enrichment (#402)**: The emission of CDISC-compliant null-flavor placeholders (e.g. indicating missing data reasons such as `MSNG`, `NA`, or `UNK`) and the export-time enrichment of coding assignments (resolving verbatim strings dynamically to `CMDECOD`, `AEDECOD`, and `MHDECOD` using dictionary terms mapped in active `ClinicalCodingLedger` entries) is owned strictly under **#402**. This prevents the emission of raw database nulls or coerced zeroes, guaranteeing submission data completeness and scientific accuracy.

---

## 5. SDTM/ADaM-Specific Privacy Policy (ADR-108)

Unlike generic, document-level redaction rules (which default to flat 365-day shifts or random ±30-day narrative text deltas, potentially breaking cross-document patterns), the biostatistical pipeline enforces a highly specialized, **deterministic SDTM/ADaM Privacy Policy** governed by **ADR-108**. This policy ensures absolute longitudinal and referential consistency across separate domains, datasets, and successive export calls.

### 1. Deterministic Pseudonymization
- **Mechanism**: Subject identifiers (`USUBJID`, `SUBJID`) and site identifiers (`SITEID`) are pseudonymized using **HMAC-SHA256** over the verbatim values.
- **Keying**: The hash is keyed by a secure, study-specific runtime salt: `BIOSTAT_EXPORT_SALT`.
- **Outcome**: A stable 64-character hexadecimal string is outputted. Because the hashing is deterministic, subject identifiers match perfectly across different datasets (e.g. DM, AE, ADSL) and subsequent export runs, preserving relational integrity (`RDOMAIN` / `IDVARVAL` joins) while fully isolating patient identity.

### 2. Stable Per-Subject Date Shifting
- **Offset Derivation**: A stable, numeric integer offset in the range `[-365, 365]` days is derived deterministically for each subject from their original `USUBJID` via HMAC-SHA256 keyed by the export salt:
  $$\text{Offset} = (\text{int}(\text{HMAC}(\text{original\_usubjid}, \text{salt}), 16) \pmod{731}) - 365$$
- **SDTM Precision-Preserving Date Shifting**: SDTM string dates (e.g. `AESTDTC`, `RFSTDTC`, `CMSTDTC`, `LBDTC`) are shifted using a precision-preserving algorithm. If a date is partial (e.g., `2026-08` or `2026-08-UN`), numeric components are shifted while leaving imprecise placeholders untouched. This guarantees that relative chronological ordering (e.g., `AEENDTC >= AESTDTC`) remains fully intact.
- **ADaM Numeric Date Shifting**: ADaM numeric SAS-integer dates (e.g. `TRTSDT`, `ASTDT`, `AENDT`) are shifted by adding the calculated subject-specific integer offset directly to the SAS day integer value.

### 3. Age Generalization
- **Mechanism**: Subject age values (both SDTM numeric `AGE` and derived variables) are capped. Any age exceeding 89 is automatically generalized and set to `89`.

### 4. Cryptographic Key Ownership
- **Sponsor Key Ownership**: The study sponsor holds exclusive ownership of the `BIOSTAT_EXPORT_SALT` cryptographic key. Keys are managed securely via runtime environment variables and must be rotated periodically in accordance with security standard operating procedures. The salt is never logged, exposed, or written to exception reports.

---

## 6. Traceability and Cross-Reference Log

To preserve platform compliance and verify requirements across the biostatistical export pipeline, the following table lists active traceability mapping references:

| Requirement / Issue ID | Description | Target Module / Verification Pathway | Status |
| :--- | :--- | :--- | :--- |
| **#402** | SDTM export data quality: null flavors and coding-assignment integration. | `apps/execution/biostat/terminology.py` & `extractors.py` | Supported |
| **#403** (ADR-108) | SDTM/ADaM export privacy: deterministic pseudonymization and date de-identification. | `apps/execution/biostat/deid.py` & `tests/test_biostat_deidentification.py` | Supported |
| **#405** | Expose authenticated SDTM/ADaM Dataset-JSON export endpoints. | `apps/execution/main.py` & `tests/test_biostat_exports.py` | Supported |
| **#407** | SDTM foundation models (Dataset-JSON structure and Pydantic v2 schemas). | `apps/execution/biostat/models.py` & `tests/test_biostat_export.py` | Supported |
| **#719** | Propagate generated SDTM SUPP-- datasets through Dataset-JSON exports. | `apps/execution/biostat/serializer.py` & `extractors.py` | Supported |
| **ADR-094** | Pure-Python Declarative Mapping Table & Pipeline architecture. | `apps/execution/biostat/mappings.py` & `tests/test_biostat_export.py` | Supported |

---

# Data Lifecycle Specification: eISF Document Lifecycle

## 1. Overview
The electronic Investigator Site File (eISF) Document Lifecycle is an automated and site-isolated workflow designed to manage investigator site files and binders securely, complying with FDA 21 CFR Part 11 and GCP guidelines.

## 2. Document & Binder States
Documents in the eISF progress through the following status values:
- **PENDING**: The default state of newly uploaded or synchronized documents, awaiting confirmation or sync propagation.
- **SYNCED**: Successfully matched and synchronized between eISF and eTMF.
- **DELETED**: Documents are logically deleted by appending a deletion record, preserving history for Part 11 auditing.

## 3. Role-Based Access Control (RBAC) Gates
Operations on eISF documents are restricted based on OIDC roles and permissions:

| Target Operations | Allowed Actor Roles | Required Permissions |
| :--- | :--- | :--- |
| **Create Document** | `site investigator`, `crc`, `admin` | `eisf_document:create` |
| **View/Download** | `site investigator`, `crc`, `auditor`, `admin` | None (gated via read check) |
| **Update Document** | `site investigator`, `crc`, `admin` | `eisf_document:update` |
| **Delete Document** | `site investigator`, `crc`, `admin` | `eisf_document:delete` |
| **Sync Documents** | `site investigator`, `crc`, `admin`, `system` | `eisf_document:sync` |

## 4. Completeness Logic & EXPECTED Binder Sections
The system tracks completeness of the electronic Investigator Site File (eISF) binder by comparing uploaded classifications against standard binder sections defined under `REQUIRED_BINDER_SECTIONS`:
- **Investigator & Staff**: CV, Delegation of Authority Log, Financial Disclosure, Medical License.
- **Protocols & Amendments**: Approved Protocol, Protocol Sign-off.
- **Regulatory Approvals**: IRB Approval, FDA Form 1572.

## 5. Audit Trail & 21 CFR Part 11 Compliance (`ISFAuditLog`)
Every operation (views, downloads, edits, sync, deletions, and completeness checks) triggers an append-only entry in the database-backed `ISFAuditLog` ledger tracking:
- Actor ID and Roles.
- Action (e.g. `CREATE_DOCUMENT`, `VIEW`, `DOWNLOAD`, `UPDATE_DOCUMENT`, `DELETE_DOCUMENT`, `COMPLETENESS`, `SYNC`).
- Part 11 change justification reason (mandatory, minimum 10 characters).
- Timestamp and record references.
Cross-site access attempts trigger high-priority `SECURITY_ALERT` events in the ledger.

## 6. Synchronization Boundary & Dependencies
The eISF service implements a robust bidirectional offline and service-to-service synchronization pipeline:
- **eISF-local Sync**: Implements duplicate detection, conflict resolution policies (`CLIENT_WINS`, `SERVER_WINS`, `MERGE`), and echo-loop prevention. This is fully implemented and tested (under `tests/test_eisf_sync.py`).
- **Open eTMF Contract (#343)**: The receiving-side synchronized document deduplication contract on the eTMF service remains an open, pending dependency.
- **Redacted Derivative Constraint (#693)**: Sync propagation is strictly limited to redacted/de-identified derivatives to avoid leaking any PHI or sensitive client data across boundaries.

---

# Data Lifecycle Specification: ePRO / Subject Portal Offline Sync

## 1. Overview
The offline ePRO (electronic Patient-Reported Outcome) and eCOA (electronic Clinical Outcome Assessment) synchronization system manages participant-reported diary submissions with high data-integrity standards, in full alignment with **PRD-EDC-007**, **PRD-EDC-008**, and **SRS Trace-9**. Its technical design and conflict resolution models are formally governed by [ADR-116](./adr/2026-08-07-epro-sync-durable-reconciliation.md).

Offline participant diaries are captured locally in the Patient/Subject Portal Progressive Web App (PWA) client and synchronized securely to the Interoperability Service (Interop) via the central API Gateway.

## 2. Sync States & Statuses
The lifecycle of an offline-captured ePRO entry progresses through the following sequential states:
- **QUEUED**: The submission is logged locally inside the client's IndexedDB queue on the patient's device, assigned a monotonic `sequence_number` and a unique `client_id`.
- **SYNCED** (or `CREATED` / `UPDATED_CLIENT_WINS` / `MERGED`): The submission is successfully transmitted to the backend Interop service and reconciled with the database.
- **CONFLICT**: Resolved deterministically on the server via conflict resolution strategies:
  - `CLIENT_WINS`: Incoming offline entry overwrites the server record.
  - `SERVER_WINS`: Existing server record is preserved.
  - `MERGE`: Overlapping and independent fields from both the client and server are merged using Last-Write-Wins (LWW) and tiebreak logic.
- **DEFEATED**: Payloads that were overwritten (during `CLIENT_WINS`) or ignored (during `SERVER_WINS`) are durably persisted in the `EPROSubmissionDefeated` table as "defeated by online-merge conflict resolution" to ensure no raw clinical data is silently discarded.
- **IGNORED**: Inbound duplicate transmissions or entries superseded by newer local sequence numbers.
- **STRUCTURAL_CONFLICT**: Triggered when a submission targets a missing or deleted clinical schema object (e.g. non-existent Instrument or SubjectAssignment). The record is rejected from primary tables, written to `EPROSubmissionDefeated`, and automatically spawns an `OPEN` `ClinicalQuery` with the system exception reason `SYSTEM SYNC EXCEPTION TRIGGERED`.

## 3. Offline Synchronization Flow
The following diagram illustrates the offline-to-online synchronization pipeline, routing gateway scopes, and target reconciliation handlers:

```mermaid
graph TD
    A[Subject Portal PWA - client] -->|Enqueues Offline Diary| B[(IndexedDB: sync-queue.js)]
    B -->|Reconnection: Flushes Payload| C[API Gateway - Subject-scoped Routing]
    C -->|REST POST /api/v1/interop/epro/submit or epro/sync| D[Interop Service]
    D -->|Calls: reconcile_records| E{Target Objects Exist?}
    E -->|Yes: Normal Merge/LWW| F[EPROSubmission]
    E -->|No: Structural Query Opened| G[ClinicalQuery status=OPEN]

    F -->|Superseded / Defeated records| H[EPROSubmissionDefeated]
    G -->|Defeated record state stored| H
```

## 4. Roles & Access Matrix
System access is strictly role-governed to isolate clinical subject boundaries from sponsor administrators and CRAs:

| Role / Scope | Allowable API Actions | Gateway Scope Constraint |
| :--- | :--- | :--- |
| **Subject** | `epro/submit`, `epro/sync`, retrieve own assignments | Scope restricted strictly to user's OIDC sub-claim / patient pseudonym |
| **Site Staff (CRC/Investigator)** | View resolved submissions, manage Clinical Queries | Restricted to assigned clinical site boundaries |
| **Sponsor Monitor (CRA/DM)** | Read-only compliance metrics, resolve structural queries | Global or site-allocated administrative read scope |

## 5. Audit Trail & 21 CFR Part 11 Compliance
Every transition, merge decision, or exception is chronologically logged in the `InteropAuditLog` using compliant append-only logs. The system records the following specific event types:
- `EPRO_SUBMIT`: Standard entry logging for individual incoming records.
- `EPRO_BULK_SYNC`: Triggered on processing batch queues, capturing total tallies of processed, created, merged, and failed sync runs.
- `EPRO_RECONCILE`: Audit logging of deterministic conflict resolution decisions (`CLIENT_WINS`, `SERVER_WINS`, or `MERGE`) and version index increments.
- `EPRO_STRUCTURAL_CONFLICT`: Logged on system exceptions, capturing missing schema identifiers and recording the mandatory change reason `SYSTEM SYNC EXCEPTION TRIGGERED`.

## 6. Delivered Implementations & Code Traceability
Because client-side JavaScript testing is excluded from the Python-based RTM generator, the verified client-side components and their human-readable traces are listed here:
- **Local Persistence & Service Worker**:
  - `apps/subject-portal/sync-queue.js` (IndexedDB queue ordering and offline state tracking)
  - `apps/subject-portal/index.js` (State persistence and reconnection-based auto-flushes)
  - `apps/subject-portal/public/sw.js` (PWA service-worker caching and offline network fallback)
- **Client Verification Suites**:
  - `apps/subject-portal/tests/portal.test.js` (Validates queue serialization and sequence ordering)
  - `apps/subject-portal/tests/portal-ecoa-regression.test.js` (Ensures robust service worker interceptors)

## 7. Planned / Pending Implementation

### Feature #389: AES-GCM Local Encryption & Per-Record Signatures (Future Scope)
- **Status**: Pending (Open under **#389**)
- **Target standard**: Future extension of **PRD-EDC-007**
- **Description**: Currently, local offline records stored inside the client's IndexedDB browser storage are held as plaintext, and the client-side queue does not perform local cryptographic signing. The complete AES-GCM-at-rest encryption layer and cryptographic per-record client signatures remain explicitly out of scope for the current system release and are tracked for implementation under Feature **#389**.
