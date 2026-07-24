# Product Requirements Document (PRD) - Cadence Clinical Platform

## 1. Document Control & Regulatory Alignment

### 1.1 Document Metadata
* **Document ID:** CAD-PRD-001
* **Version:** 1.0.0-RELEASE
* **Effective Date:** 2026-07-22
* **Classification:** GxP Confined / Highly Confidential
* **Authors:** Frederick de Ruiter (Lead Solutions Architect), Jules (Staff Software Engineer)
* **Standards Alignment:**
  * **ISO/IEC/IEEE 29148:2018:** Systems and software engineering — Life cycle processes — Requirements engineering
  * **ISO 14155:2020:** Clinical investigation of medical devices for human subjects — Good clinical practice (GCP)
  * **21 CFR Part 11 / EU Annex 11:** Electronic Records and Electronic Signatures
  * **CDISC USDM v3.0/v4.0:** Unified Study Definition Model

### 1.2 Revision History
| Version | Date | Description of Change | Author(s) | Reviewer(s) / Approver(s) |
| :--- | :--- | :--- | :--- | :--- |
| 0.1.0-DRAFT | 2026-06-01 | Initial skeleton and placeholder generation | F. de Ruiter | SDLC Steering Group |
| 1.0.0-RELEASE| 2026-07-22 | Comprehensive expansion of all clinical modules, field-level validation, clinical state machines, unblinding protocols, and query lifecycles. Conforms to ISO 29148. | Jules | F. de Ruiter, QA Directorate |

### 1.3 Signature & Approval Matrix
By signing below, the representatives from clinical development, software engineering, and regulatory affairs authorize this document as the definitive functional baseline for the Cadence Clinical Platform.

```
Clinical Product Lead:     ___________________________   Date: _______________
QA & Validation Director:  ___________________________   Date: _______________
Chief Technology Officer:  ___________________________   Date: _______________
```

---

## 2. Executive Summary & System Objectives

### 2.1 Scope of the Platform
The Cadence Clinical Platform is a unified, cloud-native clinical trial system that eliminates the classic chasm between upstream Protocol Design (Metadata Repository / MDR) and downstream Clinical Trial Execution (Electronic Data Capture / EDC).

Traditionally, clinical research metadata is isolated in static text documents and converted manually into data collection screens, leading to translation errors, compliance failures, and long timelines. Cadence Clinical integrates both environments via a single, digital data flow (DDF) utilizing the CDISC USDM standard. This system is designed for high-concurrency, GxP-compliant execution in multi-site global trials.

```
+---------------------------------------------------------------------------------+
|                                CADENCE CLINICAL                                 |
+-------------------------------------------------+-------------------------------+
|             DESIGNER SERVICE (MDR)              |    EXECUTION ENGINE (EDC)     |
|   - CDISC USDM Graph Model (Neo4j)              |    - Transactional (PostgreSQL)  |
|   - Biomedical Concepts & Trial Configurations   |    - Subject State Tracking   |
|   - Immutable Versioning & Path Branching       |    - Form Capture & Parsing   |
+-------------------------------------------------+-------------------------------+
|                                  AUTH GATEWAY                                   |
|   - Keycloak OIDC, Role-Based Access Control, Masking/PII Obfuscation           |
+---------------------------------------------------------------------------------+
```

### 2.2 Functional Hierarchy & Bidirectional Traceability
To ensure complete software validation under GxP, requirements in this document are categorized with unique alphanumeric identifiers mapped to the Quality Assurance (QA) & Validation Plan (CAD-QAP-006) and the Technical Design Document (CAD-TDD-002).

* **`PRD-SYS-XXX`**: System Baseline, Authentication, and Audit Ledger Rules
* **`PRD-MDR-XXX`**: Study Design, Biomedical Concepts, and MDR Specifications
* **`PRD-EDC-XXX`**: Electronic Data Capture, parsing, skipping, and offline synchronization
* **`PRD-SUB-XXX`**: Subject Lifecycle, Randomization, and State Transition Machines
* **`PRD-QRY-XXX`**: Query Lifecycle, Data Review, Verification, and Medical Monitoring

### 2.3 Regulatory Compliance Mandates (21 CFR Part 11 / EU Annex 11)
The system satisfies all relevant mandates for electronic records and signatures:
1. **Validation of Systems (§ 11.10(a)):** Reproducible system state configurations captured chronologically.
2. **Protection of Records (§ 11.10(c)):** Multi-AZ replication with Point-in-Time Recovery (PITR).
3. **Audit Trails (§ 11.10(e)):** Complete logging of all changes, deletions, and updates without allowing modification of the logs themselves.
4. **Authority Checks (§ 11.10(g)):** Explicit, role-based authorization for all write/edit operations.
5. **Electronic Signature Validation (§ 11.50):** Multi-factor re-authentication for clinical signs-offs.

---

## 3. System Baseline & Global Inheritance Model

### 3.1 Universal Data Entity Capabilities
Every clinical and structural entity created within the Cadence Clinical Platform (e.g., protocol elements, subjects, eCRF forms, medical terms, queries) inherits a set of core database fields, validation behaviors, and audit controls. This guarantees that developers do not have to write custom compliance overrides per module.

```
+----------------------------------------------------------------------------+
|                          UNIVERSAL INHERITED MODEL                         |
+----------------------------------------------------------------------------+
|  Fields:                                                                   |
|   - UUID (v4)                 - created_at (UTC Timestamp)                 |
|   - created_by (OIDC Sub)     - reason_for_change (Varchar(1000))          |
|   - version_index (INT >= 1)  - is_deleted (Boolean, default FALSE)        |
+----------------------------------------------------------------------------+
|  Behaviors:                                                                |
|   - soft_delete() logic intercepts SQL DELETE queries.                     |
|   - auto_increment(version_index) triggered upon any entity modification.  |
|   - audit_trail_trigger() copies pre/post state to shadow schema tables.    |
+----------------------------------------------------------------------------+
```

#### PRD-SYS-001: Standard Audit Logging (21 CFR Part 11 § 11.10(e))
The platform must enforce the presence of exactly four audit fields on every database table representing clinical execution or metadata state:
* `created_at`: Datetime in ISO 8601 UTC format (`YYYY-MM-DDTHH:MM:SS.mmmmmmZ`) generated by the database server upon commit.
* `created_by`: Globally unique OIDC user identifier string (`sub`) of the authenticated user performing the write.
* `reason_for_change`: A mandatory string of minimum 10 characters and maximum 1000 characters. If the mutation is system-generated, this must store a system-code reference.
* `version_index`: An integer representing the version sequence of the record, beginning at `1` and auto-incrementing sequentially by `1` with every update.

#### PRD-SYS-002: Soft-Delete Enforcement and Shadow Schema Preservation
No transactional clinical data or metadata may be physically purged from the persistent storage engine (PostgreSQL or Neo4j) via standard user operations.
* When a deletion is triggered, the system must set the `is_deleted` attribute to `TRUE`, update the `version_index` by `1`, log the `reason_for_change` and the executing user `created_by`.
* Direct `DELETE` statements executed at the database layer must be intercepted by database-level triggers (`BEFORE DELETE`) which automatically cancel the operation and direct the transaction to insert the delta with an active state of deleted into a secure, isolated `shadow` schema.

#### PRD-SYS-003: Cryptographic Ledger Hashing & Chain Validation
* Every audit log event block generated within the system must be cryptographically hashed using SHA-256.
* Each block must chain itself to the preceding block using the formula:
$$\text{Block\_Hash}_n = \text{SHA256}(\text{Block\_Data}_n \parallel \text{Block\_Hash}_{n-1})$$
* A background verification service must run every 60 seconds. If any block's recalculated hash does not match its stored hash, or if a block in the chain is missing, the system must transition to a read-only GxP-Lock state and generate high-priority security alerts.

### 3.2 Role-Based Access Control (RBAC) Matrix
The system governs all resource mutations via strict Role-Based Access Control (RBAC).

| Role | Access Level | Permitted Actions | Restrictions |
| :--- | :--- | :--- | :--- |
| **Sponsor Clinical Designer** | Designer Read/Write | Create/Edit Biomedical Concepts, Protocols, Forms, Arms | Cannot view Subject Data, cannot perform Randomization unblinding |
| **Site Principal Investigator (PI)** | Execution Read/Write | Screen, Enroll, and Sign Subject eCRFs, Raise Manual Queries | Cannot modify Study Protocol configurations, Restricted to own site |
| **Clinical Research Associate (CRA)** | Execution Read/Write | Perform SDV, Raise/Close Manual Queries, View Screening Logs | Cannot modify data entries, cannot perform Electronic Signatures |
| **Data Manager (DM)** | Execution Read/Write | Perform Medical Review, Raise Queries, Import Coding Dictionaries | Cannot perform PI signatures, cannot modify source subject entries |
| **Unblinded Statistician** | Execution Unblinded Read | View treatment allocation codes, run interim analysis reports | Cannot perform data entry, completely isolated from site operations |
| **System Auditor** | Read-Only | View Audit Ledger, raw database logs, and configuration graphs | Complete read-only block across all services |

#### PRD-SYS-004: Universal Site Isolation Constraint
Site users (PIs, Study Coordinators) must be mathematically restricted to subject data belonging specifically to their assigned `site_uuid`. Any API call attempting to read or write to a subject belonging to another site must return a `403 Forbidden` error and write a security alert containing the user's UUID and IP address to the audit log.

#### PRD-EDL-001: Data-Driven Expected Document Lists (EDLs) & Completeness Tracking
The system must implement a data-driven Expected Document List (EDL) reference data model and endpoints (`/api/v1/etmf/edl` and `/api/v1/etmf/completeness`) to track required study-scope and site-scope documents against clinical milestones. This replaces hardcoded milestone mappings with a data-driven configuration. Each `ExpectedDocument` record must include the four standard Part 11 audit fields as defined in `PRD-SYS-001` (specifically `created_at`, `created_by`, `reason_for_change`, and `version_index`).

#### PRD-TMF-001: TMF Taxonomy Catalog Hierarchy and Version Selection
The system must support loading different versions of the versioned DIA TMF Reference Model (e.g., v3.2.0, v4.0.0) from the `tmf_reference_model` taxonomy package in memory. Consumers must be able to retrieve any registered catalog version or set the active default catalog version dynamically.

#### PRD-TMF-002: Strict Taxonomy Validation and Ingestion Rejection
During document ingestion or modification, the eTMF engine must perform strict hierarchical verification against the active catalog taxonomy version. If the provided zone_code, section_code, or artifact_code are unknown or create an invalid/mismatched combination, the transaction must be aborted and rejected with an HTTP 422 error.

#### PRD-TMF-003: Taxonomy Version and Artifact Persistence
Every ingested document successfully validated against the active catalog must persist the exact `taxonomy_version` and canonical `artifact_code` alongside standard metadata fields inside the relational eTMF database, providing permanent, version-specific traceability of the classification.

#### PRD-TMF-004: Catalog-Driven Completeness and Milestone Alignment
Completeness audits and expected document list seeding must dynamically query mandatory artifacts per milestone directly from the catalog's public APIs (e.g., `get_mandatory_artifacts`), matching exact canonical `artifact_code` identities across study and site scopes.

---

## 4. Study Design & Clinical Metadata Repository (MDR)

### 4.1 Biomedical Concepts (BCs) and Value-Level Metadata (VLM)
Biomedical Concepts (BCs) represent the core, standardized data variables defined by the organization (e.g., Systolic Blood Pressure, Alanine Aminotransferase). Value-Level Metadata (VLM) defines the constraints, ranges, and data types governing these concepts.

```
+-------------------------------------------------------------------------------+
|                        BIOMEDICAL CONCEPT (BC) STRUCTURE                      |
+-------------------------------------------------------------------------------+
|  Concept Code: BC-VIT-001 (Systolic Blood Pressure)                           |
+-------------------------------------------------------------------------------+
|  VLM Constraints:                                                             |
|   - Data Type: Integer                                                        |
|   - Valid Range: 50 mmHg to 250 mmHg                                          |
|   - Soft-Warning Limit: < 90 mmHg or > 140 mmHg (triggers investigator alert)  |
|   - CDASH Mapping: VS.VSSPB                                                    |
+-------------------------------------------------------------------------------+
```

#### PRD-MDR-001: Value-Level Metadata Constraint Propagation
* When a study designer links a BC to an eCRF data field, the associated VLM rules (data type, range limits, unit lists) must automatically generate the corresponding JSON Schema or OpenRosa validation logic.
* Range validation must support three tiers:
  1. **Strict Rejection Range:** Submissions containing values outside these boundaries are blocked immediately at the UI level.
  2. **Soft-Warning Range:** Submissions are accepted, but require the user to input an immediate `reason_for_change` explaining the clinical outlier before saving the form.
  3. **Normal Range:** Standard clinical values needing no warning.

#### PRD-MDR-002: Biomedical Concept Lock State during Active Studies
* A Biomedical Concept that is currently referenced by an active, recruiting clinical trial protocol (defined as study status `Active-Recruiting`) is locked against modifications.
* Any attempt to update, rename, or delete a locked BC must throw a `409 Conflict` error.
* To modify a locked BC, the study designer must initiate a formal **Protocol Amendment** workflow. This spawns a new branched version of the metadata graph (e.g., Version `2.0.0-AMENDMENT`), leaving the active Version `1.0.0-RELEASE` untouched.

### 4.2 Complex Trial Designs (Adaptive, Basket, Umbrella, Platform)
The Cadence Clinical MDR supports next-generation trial frameworks where study components can be dynamically shifted, added, or removed based on interim analysis results.

```
+-------------------------------------------------------------------------------+
|                      COMPLEX TRIAL ARMS DESIGN ENGINE                         |
+-------------------------------------------------------------------------------+
|  1. Umbrella Trial Flow:                                                      |
|     Subject Screened ---> Biomarker A+ ---> Arm A (Cohort 1)                   |
|                      ---> Biomarker B+ ---> Arm B (Cohort 2)                   |
|                                                                               |
|  2. Adaptive Minimization:                                                     |
|     Interim Analysis (Day 30) ---> Checks Efficacy Threshold                  |
|                               ---> Success: Open Arm C                        |
|                               ---> Futility: Close Arm A (Failsafe Reroute)   |
+-------------------------------------------------------------------------------+
```

#### PRD-MDR-003: Dynamic Cohort Opening & Closing Rules
* The Designer Service must track trial arms and cohorts as versioned nodes in the Neo4j graph database.
* An external statistician or system API can trigger a status change for a cohort node (e.g., `Cohort_A.status = CLOSED_FOR_FUTILITY`).
* Upon receiving this mutation, the Execution Engine must automatically:
  1. Deactivate the randomization allocation path for Cohort A within 100 milliseconds.
  2. Route all future subjects satisfying the biomarker criteria to the alternative pre-configured cohort (Cohort B) or transition them to a `Screen Failed` status.

#### PRD-MDR-004: Crossover Timeline Mapping & Arm Interventions
* The MDR must support multi-period crossover trials. The trial design must model sequential periods (e.g., Period 1: Treatment A, Washout: 14 days, Period 2: Treatment B).
* Chronological sequence logic must enforce that a subject cannot enter Period 2 until the system confirms the absolute completion of Period 1 and the exact expiration of the 14-day washout period (calculated using UTC timestamps from the final Period 1 visit).

### 4.3 Blinding and Masking Rules
Blinding is critical to clinical trial integrity. Cadence Clinical maintains a cryptographically secure separation of treatment allocation metadata.

#### PRD-MDR-005: Dual-Key Blinding Security
* Treatment allocation tables matching `subject_uuid` to `treatment_group` (e.g., Drug vs. Placebo) must be encrypted using AES-256 with a unique salt.
* The decryption key must be split into two separate cryptographic shares using Shamir's Secret Sharing Scheme.
* Share A is held by the **Lead Unblinded Statistician**, and Share B is held by the **Independent Data Monitoring Committee (IDMC)**.
* No system administrator, site investigator, or clinical monitor can access the plain-text mapping without both shares being cryptographically combined via the gateway API.

#### PRD-MDR-006: Blinding Constraints on UI Data Rendering
* For double-blinded studies, any API endpoint returning subject records or visit logs to site investigator roles, clinical monitors, or sponsor study managers must dynamically redact the fields: `treatment_group`, `randomization_seed`, and `investigational_product_id`.
* The system must return a masked placeholder (e.g., `******`). Any attempt to inspect the HTTP network payload in the browser must reveal only the encrypted ciphertext of these parameters.

### 4.4 Inclusion & Exclusion (I/E) Criteria Governance
I/E criteria represent the gating rules that determine whether a subject is permitted to participate in the clinical trial.

```
+-------------------------------------------------------------------------------+
|                          INCLUSION / EXCLUSION ENGINE                         |
+-------------------------------------------------------------------------------+
|  Inclusion Rules:                                                             |
|   - INC-001: Informed Consent signed? [MUST BE 'YES']                         |
|   - INC-002: Age >= 18?                 [MUST BE 'YES']                       |
|                                                                               |
|  Exclusion Rules:                                                             |
|   - EXC-001: History of renal disease?  [MUST BE 'NO']                        |
|                                                                               |
|  Outcome:                                                                     |
|   - Any single failure immediately locks subject state to SCREEN_FAILED.      |
+-------------------------------------------------------------------------------+
```

#### PRD-MDR-007: Logical Mapping of I/E Criteria to eCRF Fields
* Every I/E criterion node in the MDR must contain a strict logical mapping expression referencing one or more specific eCRF field identifiers.
* *Example Expression:* `eCRF.DM.AGE >= 18 && eCRF.IC.SIGN_STATUS == 'SIGNED'`
* During the Screening Visit data entry, the EDC execution engine must execute these expressions in real-time. If any expression evaluates to `FALSE`, the subject state must immediately transition to `Screen Failed` and the user must be blocked from randomized treatment allocation.

---

## 5. Electronic Data Capture (EDC) & eCRF Engine

### 5.1 Spreadsheet Parsing & Sheet-to-Form Mapping
The Cadence Clinical platform ingests clinical metadata directly from standardized Excel spreadsheets, converting them into GxP-compliant OpenRosa/Enketo-compatible XForm schemas.

```
Spreadsheet (.xlsx) ──► Ingestion Parser (Brotli compression) ──► JSON Schema / XForm AST ──► SQLite/IndexedDB Storage
```

#### PRD-EDC-001: Spreadsheet Ingestion Sheet Structure
The spreadsheet parsing engine must expect an Excel file containing exactly three sheets with fixed, case-sensitive names:
1. **`FORMS`**: Defines the overall form headers, layout rules, and standard CDASH classifications.
2. **`GROUPS`**: Defines repeating and non-repeating section containers within each form.
3. **`ITEMS`**: Defines individual question fields, data types, units, choices, and skip patterns.

#### PRD-EDC-002: Field-Level Ingestion Validations
During the upload of an eCRF design spreadsheet, the parser must execute strict programmatic checks. The upload must fail with a detailed JSON diagnostic report listing sheet name, row number, and error type if any of the following occur:
* **Duplicate Field Names:** Multiple records in the `ITEMS` sheet share the same `item_id`.
* **Invalid Data Types:** An item specifies a data type other than `text`, `integer`, `decimal`, `date`, `datetime`, `boolean`, `choice_single`, `choice_multiple`, or `file`.
* **Missing References:** A field references a non-existent parent group in the `GROUPS` sheet, or a non-existent form in the `FORMS` sheet.
* **Circular Dependencies:** Skip logic or calculation expressions create an infinite loop between two or more fields (e.g., Field A relevant if Field B > 10, and Field B relevant if Field A is populated).

### 5.2 Dynamic Field Behaviors & Skip Logic
eCRFs must display fields dynamically depending on previous inputs to minimize user cognitive load and prevent incorrect data entries.

```
+-------------------------------------------------------------------------------+
|                       CONDITIONAL RENDERING FLOW CHART                        |
+-------------------------------------------------------------------------------+
|   Question: [Is the subject pregnant?]  (Field ID: PREG_STATUS)               |
|                                                                               |
|             +-------- Yes -------> Render: [Expected Due Date]                 |
|             |                              (Field ID: DUE_DATE, Required=True) |
|             |                                                                 |
|             +-------- No  -------> [Expected Due Date] remains HIDDEN.        |
|                                                                               |
|   Edge Case Safe-Nullification:                                               |
|   If user changes answer from 'Yes' to 'No':                                  |
|   DUE_DATE must be immediately nullified and wiped from database block.       |
+-------------------------------------------------------------------------------+
```

#### PRD-EDC-003: Dynamic Skip Logic Evaluation
* Fields with a conditional visibility constraint (defined via XPath or logical string, e.g., `relevant = "/data/PREG_STATUS = 'YES'"`) must be hidden by default in the UI layer.
* The frontend execution engine must re-evaluate all relevant expressions on any field state modification in less than 5 milliseconds.

#### PRD-EDC-004: Cascading Dependent Nullification (Orphan Data Safeguard)
* **Rule:** If a user modifies a parent field (e.g., changes `PREG_STATUS` from `YES` to `NO`) causing a currently visible child field (`DUE_DATE`) to become hidden, the system must immediately and completely purge any previously entered data in the child field from both the frontend memory state and the database payload.
* **Audit Trail Action:** The automatic nullification of the child field must be logged in the audit ledger with the system-generated reason: `"System-initiated purge of inactive child variable due to parent value mutation"`.

### 5.3 Repeating Groups & Advanced Grid Behaviors
Repeating groups represent logs where multiple entries can be made dynamically (e.g., Concomitant Medications, Adverse Events).

```
+-------------------------------------------------------------------------------+
|                            REPEATING GRID LOG SHEET                           |
+-------------------------------------------------------------------------------+
| Row # | Medication Name | Daily Dose | Unit | Route  | Start Date  | Actions  |
+-------+-----------------+------------+------+--------+-------------+----------+
|   1   | Paracetamol     |   1000     | mg   | Oral   | 2026-07-01  | [Delete] |
|   2   | Aspirin         |    325     | mg   | Oral   | 2026-07-05  | [Delete] |
|   3   | [Click to Add]  |            |      |        |             |          |
+-------------------------------------------------------------------------------+
```

#### PRD-EDC-005: Real-Time Row Ingestion and Index Tracking
* Within repeating groups, each row must be uniquely tracked using a 1-based index (e.g., `CM[1]`, `CM[2]`).
* The system must permit dynamic insertion, re-ordering, and deletion of rows.
* If a row is deleted (e.g., row 2 of 3), the system must soft-delete row 2, and preserve row indices without compacting them in the historical database schema to prevent breaking longitudinal edit checks referencing specific indices (e.g., referencing `CM[3]`).

#### PRD-EDC-006: Advanced Inputs (VAS and Interactive Body Maps)
* **Visual Analog Scale (VAS):** The system must support VAS inputs rendered as a continuous sliding scale from `0` to `100`. The output must be stored as a precise floating-point number (decimal) accurate to two decimal places (e.g., `87.34`).
* **Interactive Body Maps:** The system must support body map widgets where users tap/click specific physical zones (e.g., Right Knee, Left Shoulder). The captured payload must store an array of standard anatomical codes mapped to LOINC/SNOMED CT identifiers (e.g., `["SNOMED-27268008", "SNOMED-12794005"]`).

### 5.4 Offline Data Capture & Synchronization Engine
Global clinical trials are frequently conducted in remote sites with intermittent internet connectivity. Cadence Clinical ensures zero data loss via an offline-first browser architecture.

```
[Offline Browser Session] ──► Write to Local IndexedDB ──► Network Restored ──► Sync Engine ──► Conflict Evaluation ──► PostgreSQL Commit
```

#### PRD-EDC-007: Local IndexedDB Security & State Preservation
* When internet connectivity is lost, the EDC app must transition seamlessly into `Offline Capture Mode`. The UI must display a distinct yellow banner: `"Working Offline - Local Storage Enabled"`.
* All data entry, validation checks, and skip-logic evaluations must run locally using client-side JavaScript.
* Data must be encrypted locally in IndexedDB using AES-GCM with a temporary key derived from the user's active session token.

#### PRD-EDC-008: Conflict Resolution and Sync Reconciliation
Upon restoration of network connectivity, the system must trigger a background reconciliation process following these strict rules:
1. **Non-Conflicting Fields:** Modifications to independent forms or fields must be merged immediately.
2. **Conflicting Fields (Last-Write-Wins Rule):** If the same specific field in an eCRF (e.g., `Subject_101.Demographics.Weight`) was modified both offline and online during the disconnected window, the sync engine must compare the client-side modification UTC timestamp with the database server timestamp. The record with the more recent timestamp must win.
3. **Audit Preservation:** The overwritten record must not be deleted. It must be written directly to the `shadow` audit schema, logging both inputs, and marking the defeated entry with the status: `"Defeated by online-merge conflict resolution"`.

### 5.5 Advanced Field-Level Validation & Interactive Controls
We define precise mathematical bounds, UI representations, and JSON payload structures for advanced input widgets to eliminate ambiguity during implementation.

#### PRD-EDC-009: Visual Analog Scale (VAS) Slider Specifications
* **UI Controls:** The slider must represent a continuous scale from `0` (Left: "No Pain") to `100` (Right: "Worst Imaginable Pain"). Keyboard increments of arrow keys must alter the value by exactly `1.00` unit.
* **Database Format:** Decimal(5,2) in PostgreSQL, validated between `0.00` and `100.00`.
* **Dynamic Warning Flags:** If a slider value registers $> 80.00$, the UI must render an inline alert asking the user to confirm if this represents a Serious Adverse Event (SAE) condition.

#### PRD-EDC-010: Interactive Body Map Coordinates and Schema Mapping
* **SVG Mapping Layer:** The body map widget renders a multi-layered vector SVG comprising 74 distinct anatomical zones. Each zone holds an immutable attribute `data-snomed-id` containing its SNOMED CT clinical code.
* **Selection State:** Tapping an SVG zone toggles the `selected` class and appends the SNOMED CT code to the form state.
* **JSON Schema Payload:** The target field must serialize selections as a JSON list:
```json
{
  "field_id": "BODY_MAP_PAIN_LOCATIONS",
  "value_type": "snomed_codes",
  "selections": [
    {"code": "SNOMED-27268008", "term": "Right Knee Joint"},
    {"code": "SNOMED-12794005", "term": "Left Shoulder Joint"}
  ]
}
```

---

## 6. Subject Operations & Randomization Workflows

### 6.1 Subject Lifecycle State Machine
A subject's progress through a clinical trial is highly regulated. Subject states must undergo rigid, state-controlled transitions to prevent clinical protocol deviations.

```
                                  +-------------------+
                                  |    Screening      |
                                  +---------+---------+
                                            |
                         +------------------+------------------+
                         |                                     |
               [I/E Fail / Drop]                             [Pass]
                         |                                     |
                         v                                     v
              +-------------------+                  +-------------------+
              |   Screen Failed   |                  |     Enrolled      |
              +-------------------+                  +---------+---------+
                                                               |
                                                       [Randomization]
                                                               |
                                                               v
                                                     +-------------------+
                                                     |    Randomized     |
                                                     +---------+---------+
                                                               |
                                                          [First Visit]
                                                               |
                                                               v
                                                     +-------------------+
                                                     |      Active       |
                                                     +----+---------+----+
                                                          |         |
                                               [Complete] |         | [Withdrawal]
                                                          v         v
                                        +-------------------+     +-------------------+
                                        |     Completed     |     |     Withdrawn     |
                                        +-------------------+     +-------------------+
```

#### PRD-SUB-001: State Transition Matrix & Enforcements
The system must enforce that a subject's state can only mutate according to the strict transition rules defined below. Any attempt to bypass these paths via direct API updates must throw a `422 Unprocessable Entity` error.

| Initial State | Event Trigger | Valid Target State | Forbidden Target States | Business Logic Constraint |
| :--- | :--- | :--- | :--- | :--- |
| **None** | Initiated Screening | `Screening` | All except `Screening` | Requires input of unique screening ID. |
| **Screening** | Inclusion/Exclusion Fail | `Screen Failed` | `Enrolled`, `Randomized`, `Active` | Triggered automatically by any I/E criteria failure. |
| **Screening** | Inclusion/Exclusion Pass | `Enrolled` | `Screen Failed`, `Randomized` | All I/E rules must evaluate to `TRUE`. |
| **Enrolled** | Executed Randomization | `Randomized` | `Active`, `Completed`, `Withdrawn` | Enforces verification that Informed Consent remains active. |
| **Randomized** | Administered Dose / First Visit | `Active` | `Screening`, `Enrolled` | Subject cannot go back to screening once randomized. |
| **Active** | Completed Last Protocol Visit | `Completed` | `Screening`, `Enrolled`, `Randomized` | Requires completion of all mandatory eCRFs. |
| **Active** / **Randomized** | Subject Withdraws Consent | `Withdrawn` | All except `Withdrawn` | Irreversible state. Immediately restricts future clinical data entry. |

#### PRD-SUB-002: Partial Visit Query Capability on Withdrawn Subjects
* When a subject transitions to the `Withdrawn` state, the EDC engine must instantly lock all upcoming scheduling visits (e.g., future visits cannot be initialized).
* **Exception Rule:** Investigators and Clinical Monitors must retain full write access to answer, modify, and close outstanding data queries on visits that were already completed *prior* to the withdrawal date.

### 6.2 Randomization Rules & Minimization Algorithms
Randomization prevents selection bias. Cadence Clinical supports multiple automated randomization methods.

```
+-------------------------------------------------------------------------------+
|                         RANDOMIZATION ENGINE METRICS                          |
+-------------------------------------------------------------------------------+
|  Minimization Formula (Total Imbalance):                                      |
|                                                                               |
|  For each candidate arm (T):                                                   |
|  D_T = sum_{f in Factors} [ Count(Site(f)) + Count(Age(f)) + Count(Gender(f)) ]|
|                                                                               |
|  Select Arm T that minimizes D_T.                                             |
|  If D_A == D_B, apply a random probability seed (e.g., p=0.8 for balance).    |
+-------------------------------------------------------------------------------+
```

#### PRD-SUB-003: Stratified Block Randomization
* The system must support Stratified Block Randomization, where subjects are grouped into strata based on baseline covariates (e.g., Site ID, Age Group `<50` vs. `\ge 50`).
* Within each stratum, randomization allocation must execute according to a pre-defined block sequence configuration (e.g., block sizes of `4` containing two `A` and two `B` allocations).
* Block configurations must be encrypted at rest. The active block index must be updated within a thread-safe transaction lock to prevent race conditions during concurrent multi-site enrollments.

#### PRD-SUB-004: Dynamic Minimization Algorithm
* To handle small sample sizes or large numbers of prognostic factors, the system must support dynamic Pocock-Simon Minimization.
* When a subject is randomized, the algorithm must calculate the total covariate imbalance across all treatment arms that would result from assigning the subject to each arm.
* The subject must be assigned to the treatment arm that minimizes the resulting imbalance with a default probability weight of `p = 0.8`. A random number generator seeded with a high-entropy hardware random source must resolve the allocation.

### 6.3 Emergency Unblinding Protocols
In emergency medical situations where knowing the treatment group is critical to subject safety, the system must support a controlled, fully audited unblinding protocol.

```
+-------------------------------------------------------------------------------+
|                          EMERGENCY UNBLINDING FLOW                            |
+-------------------------------------------------------------------------------+
|  PI requests Unblinding ---> Re-Authenticate (OIDC) ---> Select Reason Code    |
|                                                                |              |
|  State changes to UNBLINDED <--- System Decrypts Allocation <----+            |
|  (Restricted future entry)                                                    |
|                                                                               |
|  Immediate GxP Alert: Emails sent to Sponsor Safety Lead, CRA, and IDMC       |
+-------------------------------------------------------------------------------+
```

#### PRD-SUB-005: Triggering and Authorizing Emergency Unblinding
* Emergency unblinding must be accessible only via a dedicated, secure action panel in the Subject View interface.
* The user (Principal Investigator or Authorized ER Physician) must perform the following actions:
  1. Re-enter their active OIDC password.
  2. Select a GxP Reason Code from a pre-defined list (e.g., `SAE-Life-Threatening-Event`, `Accidental-Overdose`, `Required-by-Regulatory-Authority`).
  3. Enter a detailed text description of minimum 50 characters explaining the clinical necessity.
* Once submitted, the system must immediately decrypt the treatment allocation for that specific subject and display it on screen for exactly 120 seconds.

#### PRD-SUB-006: Immediate Unblinding State Mutation & System Actions
* Upon unblinding execution, the subject's baseline state must instantly mutate to `Unblinded` (represented by a global database flag `is_unblinded = TRUE`).
* The system must immediately trigger the following events:
  1. **Notification Dispatch:** Send automated, high-priority GxP email and SMS alerts to the **Sponsor Safety Lead**, the assigned **Lead Monitor (CRA)**, and the **IDMC Secretariat**.
  2. **Data Restriction:** Restrict further standard double-blinded data entry for this subject. The investigator can only complete safety/AE forms.
  3. **Audit Ledger Entry:** Write an immutable ledger block capturing: investigator user ID, timestamp, reason, and decryption key verification.

### 6.4 Re-Consent Triggers & Protocol Amendments
During a trial, amendments to the study protocol (e.g., changes to dose levels, safety risks) often require subjects to sign a new Informed Consent Form (ICF).

#### PRD-SUB-007: Re-Consent Gating on Visits
* When a protocol amendment is published (e.g., Version `2.0.0`), the system must permit the Study Designer to tag the amendment as `"Requires Re-Consent = TRUE"`.
* Once this configuration is active, the Execution Engine must block the PI from entering data into any subsequent clinical visits for existing subjects under the new protocol version until they upload a signed, dated ICF matching the exact version code of the amendment.
* The UI must render a prominent blocking modal: `"Re-Consent Required - Demographics & Visit Forms Locked"`.

---

## 7. Query Management & Data Review Workflows

### 7.1 Query Lifecycle & State Machine
Queries are discrepancy flags raised when data values are missing, inconsistent, or clinically improbable. Queries can be raised either automatically by the system (edit checks) or manually by clinical monitors or data managers.

```
                  +----------------------------------------------------+
                  |                    Opened                          |
                  +---------+--------------------------------+---------+
                            |                                |
                   [Investigator Answers]           [System Auto-Resolves]
                            |                                |
                            v                                v
                  +-------------------+            +-------------------+
                  |     Answered      |            |      Closed       |
                  +---------+---------+            +-------------------+
                            |
                     +------+------+
                     |             |
                [CRA Approves]  [CRA Rejects]
                     |             |
                     v             v
            +-----------------+   +-----------------+
            |     Closed      |   |    Reopened     |
            +-----------------+   +-----------------+
```

#### PRD-QRY-001: Query State Transitions and Constraints
The query engine must enforce transitions according to the strict state table below. Any illegal transition request must return a `400 Bad Request` error.

| Source State | Event / Actor | Target State | Permitted Actions & Conditions |
| :--- | :--- | :--- | :--- |
| **None** | Edit Check Fails (System) | `Opened` | System automatically inserts query node; sets `created_by = "SYSTEM"`. |
| **None** | Manual Flag (Monitor/DM) | `Opened` | Requires associated text and field-target reference. |
| **Opened** | Investigator Data Correction | `Answered` | Investigator must correct data field OR provide an explanatory text answer. |
| **Opened** | Data Change matches rule | `Closed` | For system queries: if the investigator changes data such that the edit check rule now passes, the system auto-closes the query. |
| **Answered** | CRA/Monitor Approval | `Closed` | Query is permanently resolved. Field changes are locked in active status. |
| **Answered** | CRA/Monitor Rejection | `Reopened` | Query is returned to investigator. Rejection reason text is required. |
| **Reopened** | Investigator Correction | `Answered` | Investigator provides updated correction. |

#### PRD-QRY-002: Query Escalation Rules
* If a query remains in the `Opened` or `Reopened` state for more than 14 consecutive calendar days, the system must auto-escalate the query.
* **Escalation Actions:**
  1. Append a high-priority GxP flag to the query node (`priority = "HIGH"`).
  2. Send a daily query aging digest email to the **Site Principal Investigator** and the **Sponsor Lead CRA**.

### 7.2 Discrepancy Notes & Cross-Form/Longitudinal Edit Checks
Clinical data validation frequently requires comparing data entries across different forms (cross-form) or different timepoints (longitudinal).

```
+-------------------------------------------------------------------------------+
|                        LONGITUDINAL EDIT CHECK EXAMPLES                       |
+-------------------------------------------------------------------------------+
|  Edit Check 1 (Temporal Constraint):                                          |
|  Is Adverse Event (AE) Start Date < Informed Consent (IC) Date?               |
|  Expression: eCRF.AE.START_DATE < eCRF.IC.SIGN_DATE                           |
|  Result: Trigger Query "AE date cannot precede consent."                      |
|                                                                               |
|  Edit Check 2 (Dose Escalation Constraint):                                   |
|  Is Dose(Visit 3) < Dose(Visit 2)?                                            |
|  Expression: eCRF.V3.DOSE < eCRF.V2.DOSE                                      |
|  Result: Trigger Query "Dose reduction detected. Clinical justification req."|
+-------------------------------------------------------------------------------+
```

#### PRD-QRY-003: Cross-Form Edit Check Execution
* The Execution Engine must run a background evaluation queue that processes cross-form edit checks asynchronously upon every form submission.
* *Example Rule:* The system must compare the `AE.onset_date` with the `Informed_Consent.signed_date`. If `AE.onset_date < Informed_Consent.signed_date`, the system must automatically raise a System Query on the `AE.onset_date` field within 500 milliseconds.

#### PRD-QRY-004: Longitudinal Validation and Repeat-Visit Logic
* Edit checks must be capable of traversing historical visits.
* The system must execute validation rules comparing values between Visit $N$ and Visit $N-1$ (e.g., Subject weight cannot decrease by more than 20% between two consecutive visits).
* If the previous visit data is not yet entered or is in a `Draft` state, the edit check must pause execution and enter a `Pending-Predecessor` status, resuming automatically once the predecessor visit form transitions to `Complete`.

### 7.3 Source Document Verification (SDV) and Targeted SDV (tSDV)
Source Document Verification is the process by which a clinical research associate (CRA) verifies that the electronic data entered in the eCRF matches the patient's raw medical records at the clinic.

```
+-------------------------------------------------------------------------------+
|                             tSDV CONFIGURATION MATRIX                         |
+-------------------------------------------------------------------------------+
|  Trial Profile: Low-Risk Phase IV                                             |
|  tSDV Rule:                                                                   |
|   - First 3 subjects at every site: 100% SDV (Critical Safety fields)          |
|   - Subsequent subjects: 20% random selection.                                |
|                                                                               |
|  Trigger: Subject 004 Enrolled.                                               |
|  Algorithm computes random seed ---> Selected for SDV.                         |
|  Investigator form modifications drop active SDV flag ---> CRA alerted.        |
+-------------------------------------------------------------------------------+
```

#### PRD-QRY-005: Field-Level SDV Flags and Audit Retention
* The system must render an interactive checkbox next to every eCRF field for users with the `CRA` role.
* Checking the box marks the field as "Verified" (`is_sdv_verified = TRUE`), storing the CRA's UUID and the precise verification timestamp in the field's metadata.
* **SDV Verification Preservation:** Once verified, the field status is locked in the active viewport.

#### PRD-QRY-006: Automatic Verification Drop upon Data Modification
* **Rule:** If an investigator or coordinator updates the value of a field that is currently marked as "Verified" (`is_sdv_verified == TRUE`), the system must instantly and automatically drop the verification status of that field (`is_sdv_verified = FALSE`).
* **Audit and Alert Actions:**
  1. Force the investigator to supply a mandatory GxP reason for editing verified data.
  2. Write an event log to the audit schema.
  3. Send an immediate notification flag to the assigned CRA's dashboard: `"Previously verified field modified on Subject X - Visit Y"`.

#### PRD-QRY-007: Targeted SDV (tSDV) Sampling Algorithm
* To reduce monitoring costs, the platform must support targeted SDV (tSDV) configurations defined in the MDR.
* The system must support two sampling models:
  1. **Subject-Based Sampling:** e.g., 100% SDV for the first 3 subjects at a site, followed by a randomized 20% of subsequent subjects.
  2. **Field-Based Sampling:** e.g., 100% SDV on primary safety endpoints (Adverse Events, Serious Adverse Events, Key Laboratory variables), and 0% on exploratory questionnaires.
* The selection of randomized subjects for tSDV must use a deterministic PRNG seeded with the trial's unique random seed combined with the `subject_uuid`, ensuring that the sampling status of a subject is stable and cannot be altered or gamed by site coordinators.

---

## 8. Definition of Done (DoD) & Bi-directional Traceability

This Product Requirements Document represents the definitive, legally compliant system configuration for Cadence Clinical. To satisfy the Definition of Done (DoD) for GxP system validation:

1. **Requirement Mapping:** Every requirement key (`PRD-SYS-XXX` through `PRD-QRY-XXX`) defined in this document must map directly to:
   - A database schema schema attribute or application-layer class in the **Technical Design Document (TDD)**.
   - An automated unit, integration, or manual verification script in the **Quality Assurance (QA) & Validation Plan**.
2. **Regulatory Conformity:** No features may be added or modified without updating this document and acquiring the necessary regulatory/QA signatures.
3. **Audit Readiness:** All configurations are compiled in real-time. This PRD itself is cryptographically sealed in the software build pipeline to prevent retroactive tampering or undocumented requirement modifications.
