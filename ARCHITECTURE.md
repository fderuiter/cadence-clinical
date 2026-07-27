# Architecture Specification: Cadence Clinical

## 1. System Vision & Problem Statement

Traditional clinical trial builds require manual, error-prone translation of protocol documents into downstream EDC systems. This causes multi-month setup delays, risk of discrepancies between protocols and CRFs, and expensive amendment re-work.

**Cadence Clinical** solves this by establishing a single, metadata-driven source of truth that automates the generation of downstream trial infrastructure directly from digitized protocol definitions.

---

## 2. Core Service Domains

### A. Designer Service (`apps/designer`)
* **Role:** Study Definition Repository (SDR) and Clinical Metadata Repository (MDR).
* **Datastore:** Neo4j Graph Database.
* **Core Responsibilities:**
  * Protocol structure authoring (Arms, Epochs, Branches, Visits).
  * Schedule of Activities (SoA) matrix generation.
  * CDISC USDM export endpoints.
  * Fine-grained protocol semantic versioning and amendment diffing.

### B. Execution Engine (`apps/execution`)
* **Role:** Electronic Data Capture (EDC) Runtime.
* **Datastore:** PostgreSQL Relational Database.
* **Core Responsibilities:**
  * Automatic generation of eCRF layouts and validation rules from USDM specifications.
  * Subject enrollment, matrix tracking, and event scheduling.
  * Real-time edit-check evaluation during site data entry.
  * Discrepancy (query) workflow management.

### C. Web Client (`apps/web`)
* **Role:** Primary User Interface.
* **Core Responsibilities:**
  * Renders standard clinical forms directly from XML payloads compiled by the backend translation engine.
  * Provides site investigators and data managers a unified interface for data entry and clinical study management.

### D. Shared UI Components (`packages/ui`)
* **Role:** Design System Library.
* **Core Responsibilities:**
  * Provides reusable, standardized UI components (e.g., inputs, layouts) ensuring design consistency.
  * Shared seamlessly across frontend packages using the pnpm workspace protocol.

### E. Clinical Metadata Validation & Translation (`apps/designer` and `apps/execution`)
* **Role:** Unified Standard Domain Modeling & Validation.
* **Core Responsibilities:**
  * Official CDISC USDM standard representation using the `usdm` package inside the Designer (`apps/designer/`).
  * In-memory bidirectional transformation adapters (USDM JSON ↔ OpenRosa / CDISC ODM) in the Execution engine (`apps/execution/`).

### F. Gateway & Identity (`apps/gateway`)
* **Role:** Reverse Proxy & Access Control.
* **Core Responsibilities:**
  * Keycloak OIDC JWT validation.
  * Centralized Role-Based Access Control (RBAC) mapping:
    * `Study Designer` ──► Design permissions in `apps/designer`
    * `Site Investigator / CRC` ──► Data capture permissions in `apps/execution`
    * `Data Manager` ──► Query and form management across both domains.
    * `Monitor` ──► Monitoring, site verification, and CTMS operations in `apps/ctms`
    * `Grants Manager` ──► Budget, financials, and CTMS administrative operations in `apps/ctms`

### G. Event-Driven eTMF Module (`apps/etmf`)
* **Role:** Electronic Trial Master File (eTMF) Repository and Completeness Tracker.
* **Datastore:** SQLite / PostgreSQL Relational Database.
* **Core Responsibilities:**
  * Ingests, taxonomy-classifies, and versions clinical trial artifacts mapped to DIA TMF Reference Model Zones 1-11.
  * Implements Expected Document Lists (EDLs) through the `ExpectedDocument` data model to replace static, hardcoded milestone rules.
  * Computes site-aware, data-driven milestone completeness checks by querying active EDLs and combining study-scope and site-scope required artifacts.
  * Enforces role-based access control and trial lock restrictions via the Gateway and `GatewayAuthMiddleware` to block read-only inspector roles from mutating definitions or archives.
  * Maintains a 21 CFR Part 11 compliant audit trail (`TMFAuditLog`) capturing user contexts, timestamps, and justifications for all eTMF views, downloads, EDL updates, and completeness checks.

### H. Clinical Trial Management System (`apps/ctms`)
* **Role:** Administrative, Financial, Operational, and Monitoring Workspace.
* **Datastore:** SQLite / PostgreSQL Relational Database.
* **Core Responsibilities:**
  * Trial, site, and operational metadata tracking (recruitment, milestone verification).
  * Budget and investigator grant tracking across roles like Grants Manager.
  * Integration with Keycloak OIDC authentication via the secure API gateway.
  * Full 21 CFR Part 11 compliant auditing (`CTMSAuditLog`) recording all actions, view queries, and mutations with explicit change reasons.
  * Role-Based Access Control (RBAC) ensuring write mutations are restricted to roles like `Monitor`, `Grants Manager`, `CRA`, or `Admin`, and read-only queries are restricted to authorized operational personnel.

### I. Quality & CAPA Management Service (`apps/quality`)
* **Role:** Isolated Quality Assurance & Compliance Workspace.
* **Datastore:** SQLite / PostgreSQL Relational Database.
* **Core Responsibilities:**
  * Secure, relational protocol deviation tracking linked directly to systematic root causes and corrective/preventive action models.
  * Integration with Keycloak OIDC authentication via standard headers proxying through the secure API gateway.
  * Full 21 CFR Part 11 compliant mutable record auditing with mandatory `version_index` incrementing, standard creation/traceability metadata, and a non-empty change reason header (`X-Change-Reason`).
  * Immutable, append-only, chronological quality logs (`QualityAuditLog`) of all viewed records, updates, and transitions.
  * Restricts access to read-only roles (`auditor`, `inspector`, `regulatory_inspector`) from all mutating operations, gates general write access, and authorizes terminal CAPA approvals or closures only to Quality Oversight roles (`quality_manager`, `qa_lead`, `quality_oversight`, `admin`).

### J. Interoperability & Sync Gateway Service (`apps/interop`)
* **Role:** EHR FHIR Data Adapter & ePRO Submission Sync Gateway.
* **Datastore:** SQLite / PostgreSQL Relational Database.
* **Core Responsibilities:**
  * Ingests HL7 FHIR bundles, performs PII stripping and pseudonymization, and CDASH mappings to pre-fill observations.
  * Receives single or bulk ePRO/eCOA submissions from active study subjects.
  * Implements multi-strategy offline reconciliation (`CLIENT_WINS`, `SERVER_WINS`, `MERGE`) and isolates/logs conflict details in `EPROSubmissionDefeated`.
  * Triggers auditable open clinical queries automatically for submissions with structural mismatches.
  * Computes subject compliance schedules and triggers async background notifications (EMAIL, SMS, WEBHOOK, IN_APP).

### K. Patient/Subject Portal (`apps/subject-portal`)
* **Role:** Standalone Patient-Facing ePRO Web Application.
* **Core Responsibilities:**
  * Serves as an isolated, secure Progressive Web App (PWA) client optimized for mobile environments.
  * Integrates with Keycloak OIDC for authenticated login under the strict `Subject` role.
  * Implements offline-first caching via service workers and queues signed submissions chronologically inside IndexedDB.
  * Provides a visual Sync Queue Panel allowing subjects to view transmission logs and online reconciliation decisions.

---

## 3. Data Transformation Flow

```text
[ Study Designer Authors Protocol ]
                 │
                 ▼
 [ Saved to Neo4j Graph (USDM) ]
                 │
                 ▼
 [ DDF Event: "Study Published" ]
                 │
                 ▼
 [ Transformer: USDM -> ODM/XForm ]
                 │
                 ▼
 [ Provisioned into PostgreSQL EDC ] ───► [ eCOA Instrument Definition Assigned ]
                 │                                        │
                 ▼                                        ▼
 [ Live Site Data Entry & Audit Log ]    [ Patient Completes ePRO Assessment (PWA) ]
                │                                         │
                ├─────────────────────────────────────────┘
                ▼
 [ CDASH Extractor: SDTM Domains ]
                │
                ▼
 [ Analysis Derivation: ADaM Datasets ]
                │
                ▼
 [ Serializer: CDISC Dataset-JSON 1.0 ]
                │
                ▼
 [ Validator: Conformance Verification ]
                │
                ├─────────────────────────┐
         (If Valid)                 (If Invalid)
                │                         │
                ▼                         ▼
 [ Log SUCCESS in BiostatExport ]  [ Log FAILED & Raise HTTP 422 ]
