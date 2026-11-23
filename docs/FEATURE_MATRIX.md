# Feature & Compatibility Matrix

> [!WARNING] **Status: pre-kernel baseline.** Lists features of the per-service
> design; many are not implemented in the .NET code. See
> [ARCHITECTURE.md §3](../ARCHITECTURE.md#3-running-services-today) for what
> runs today and the
> [kernel specification](kernel/CLINICAL_KERNEL_SPECIFICATION.md) for the
> target.

## 1. Feature Categories & Environment Matrix

This matrix details the distribution of core compliance and tracking features
across the primary services in the Cadence Clinical architecture.

- **Metadata Tracking** (`v1.0.0`, `Supported`) — Designer Service (Neo4j):
  Centralized CDISC USDM management. Execution Engine (PostgreSQL): Local
  execution state tracking.
- **Graph Versioning** (`v1.2.0`, `Supported`) — Designer Service (Neo4j): Full
  historical graph paths. Execution Engine (PostgreSQL): N/A (Relational state).
- **Cryptographic Sealing** (`v1.1.0`, `Supported`) — Designer Service (Neo4j):
  Chained audit node creation. Execution Engine (PostgreSQL): Background chained
  hashing.
- **Hard-Delete Capture** (`v1.0.0`, `Supported`) — Designer Service (Neo4j):
  Soft deletes via relationships. Execution Engine (PostgreSQL): Database-level
  trigger / shadow schema.
- **Masked CSV Export** (`v1.3.0`, `Supported`) — Designer Service (Neo4j):
  Structural hashing of authors. Execution Engine (PostgreSQL): Dynamic PII
  masking & hashing.
- **21 CFR Part 11 Fields** (`v1.0.0`, `Supported`) — Designer Service (Neo4j):
  Enforced on all mutations. Execution Engine (PostgreSQL): Enforced on all
  transactions.
- **eTMF Taxonomy Classification** (`v1.4.0`, `Supported`) — Designer Service
  (Neo4j): API Gateway resolution via catalog. Execution Engine (PostgreSQL):
  Enforced strict ingestion hierarchy validations.
- **CTMS Site Operational Tracking** (`v1.5.0`, `Supported`) — Designer Service
  (Neo4j): N/A. Execution Engine (PostgreSQL): Secured monitor/milestone/CRA
  workload operations & append-only audit trail.
- **Quality & CAPA Management** (`v1.5.0`, `Supported`) — Designer Service
  (Neo4j): N/A. Execution Engine (PostgreSQL): Secured protocol deviation
  tracking, Root Cause Analysis (RCA), CAPA transition workflows, and
  append-only QA audit logs.
- **CDISC SDTM Mapping** (`v1.7.0`, `Supported`) — Designer Service (Neo4j):
  N/A. Execution Engine (PostgreSQL): Authenticated SDTM/ADaM extraction,
  Dataset-JSON serialization, structural/referential validation, and
  BiostatExport audit logging.
- **ePRO Patient Diary Submissions** (`v1.6.0`, `Supported`) — Designer Service
  (Neo4j): N/A. Execution Engine (PostgreSQL): Secured REST single/bulk
  submissions with deterministic conflict strategy resolution.
- **Offline PWA Portal & Sync** (`v1.6.0`, `Supported`) — Designer Service
  (Neo4j): Independent client UI and IndexedDB queue. Execution Engine
  (PostgreSQL): N/A (Client-side execution cached via Service Workers).
- **Multi-Channel Patient Alerts** (`v1.6.0`, `Supported`) — Designer Service
  (Neo4j): N/A. Execution Engine (PostgreSQL): Automated compliance reminders
  computed from Subject Assignments via SMS/Email/Webhook.
- **Medical Coding: AE Coverage** (`v1.5.0`, `Supported`) — Designer Service
  (Neo4j): N/A. Execution Engine (PostgreSQL): Automated MedDRA dictionary
  coding and system query generation for Adverse Events (AETERM).
- **Medical Coding: MH Coverage** (`v1.5.0`, `Supported`) — Designer Service
  (Neo4j): N/A. Execution Engine (PostgreSQL): Automated MedDRA dictionary
  coding and system query generation for Medical History (MHTERM).
- **Medical Coding: CM Coverage** (`v1.5.0`, `Supported`) — Designer Service
  (Neo4j): N/A. Execution Engine (PostgreSQL): Automated WHODrug dictionary
  coding and system query generation for Concomitant Medications (CMTRT).
- **SDTM/ADaM CDISC Export** (`v1.7.0`, `Supported`) — Designer Service (Neo4j):
  N/A. Execution Engine (PostgreSQL): Secured Dataset-JSON 1.0.0 format exports
  (DM, AE, VS, LB, MH, CM, ADSL, ADAE, ADVS) with audit trails.
- **Global Library Templates** (`v1.8.0`, `Supported`) — Designer Service
  (Neo4j): Multi-versioned Forms, Data Elements, Arms, and Visits. Execution
  Engine (PostgreSQL): N/A (Referenced downstream on instantiation).
- **Multi-Tenant Scoping** (`v1.8.0`, `Supported`) — Designer Service (Neo4j):
  Metadata partitioned by validated sponsor IDs, blank context blocked.
  Execution Engine (PostgreSQL): N/A.
- **Governance & State Machine** (`v1.8.0`, `Supported`) — Designer Service
  (Neo4j): Allowed transition validations and role gates (DRAFT to ARCHIVED).
  Execution Engine (PostgreSQL): N/A.
- **In-Use Locks & Amendments** (`v1.8.0`, `Supported`) — Designer Service
  (Neo4j): In-use template mutation write blocking, formal `/amend` cloning
  workflow. Execution Engine (PostgreSQL): N/A.
- **Native Part 11 eSignatures** (`v1.9.0`, `Supported`) — Designer Service
  (Neo4j): Certificate-bound protocol-approval signing & graph version locking.
  Execution Engine (PostgreSQL): Certificate-bound document-signing with 60s
  gateway step-up token, replay prevention & immutability locking.
- **Tickets & Query Escalation** (`v1.9.0`, `Supported`) — Designer Service
  (Neo4j): N/A. Execution Engine (PostgreSQL): Secured query/ticket tracking
  including comments, status transitions, optimistic locking, and background SLA
  escalation with GxP audit logs.
- **CRF Builder** (`v1.10.0`, `In Progress`) — Designer Service (Neo4j):
  Interactive graphical canvas layout design, Global Library template
  instantiation, peer reviews, cycle simulations, and XML/PDF exports. Execution
  Engine (PostgreSQL): Local sync recovery database, FHIR pre-fill, and
  multi-channel reminders.
- **Controlled Terminology (CDISC CT)** (`v1.11.0`, `Supported`) — Designer
  Service (Neo4j): Stateless real-time CDISC CT / NCI Thesaurus search, live
  concept code verification, and terminology caching (referencing ADR-2346,
  ADR-065, and ADR-067). Execution Engine (PostgreSQL): N/A (Stateless read-only
  queries with signed headers).
- **Secure File Share & Object Storage** (`v1.12.0`, `Supported`) — Designer
  Service (Neo4j): N/A. Execution Engine (PostgreSQL): Secured direct presigned
  S3/MinIO upload/download transfers, envelope metadata in DB, cryptographic
  guest links, and FileshareAuditLog.
- **Centralized Statistical Monitoring** (`v1.13.0`, `Supported`) — Designer
  Service (Neo4j): N/A. Execution Engine (PostgreSQL): Benford's Law
  Chi-Square/MAD digit preference forensics, Mahalanobis multivariate outlier
  detection, and QA deviation escalation.
- **pHash Image Fraud Detection** (`v1.13.0`, `Supported`) — Designer Service
  (Neo4j): N/A. Execution Engine (PostgreSQL): 64-bit Discrete Cosine Transform
  (DCT) perceptual hashing detecting cross-site duplicate medical scans (Hamming
  distance $\le 5$).
- **Blinded DICOM Adjudication** (`v1.14.0`, `Supported`) — Designer Service
  (Neo4j): Imaging criteria definitions. Execution Engine (PostgreSQL): WebGL
  MPR axial/sagittal/coronal viewer, blinded dual-reader RECIST 1.1 evaluations,
  and automated 20% SOD discordance escalation.
- **Clinical AI Gateway & De-ID** (`v1.14.0`, `Supported`) — Designer Service
  (Neo4j): N/A. Execution Engine (PostgreSQL): Tier 1–3 model routing, PII/PHI
  de-identification air-gap proxy, surrogate tokens, and Part 11
  dual-attribution AI draft ledgers.
- **Site Payments & Grants** (`v1.15.0`, `Supported`) — Designer Service
  (Neo4j): Procedure fee rules & grant contract templates. Execution Engine
  (PostgreSQL): eCRF visit completion fee triggers, CRA SDV milestones,
  multi-payee grant splits, 10% statutory closeout holdback, and 1099 tracking.
- **Knowledge & Contextual SOP Hub** (`v1.15.0`, `Supported`) — Designer Service
  (Neo4j): N/A. Execution Engine (PostgreSQL): Dynamic help drawer routing by
  user persona and route pattern, specificity scoring, and two-tier versioned
  SOP document ledgers.
- **Greenlight Site Activation Engine** (`v2.0.0`, `In Progress`) — Designer
  Service (Neo4j): Country-specific prerequisite rules matrix in Designer.
  Execution Engine (PostgreSQL): Multi-factor prerequisite evaluator, HMAC
  activation tokens, and programmatic RTSM/EDC write-gating.
- **SMART on FHIR eSource Ingestion** (`v2.0.0`, `In Progress`) — Designer
  Service (Neo4j): CDASH mapping definitions. Execution Engine (PostgreSQL):
  Asymmetric private-key JWT EHR polling, split-screen CRC review drawer, and
  non-destructive 1-click reconciliation.
- **Enterprise Lab & Toxicity Grading** (`v2.0.0`, `In Progress`) — Designer
  Service (Neo4j): LOINC variable mapping & unit definitions. Execution Engine
  (PostgreSQL): Central/local lab feeds, age/sex reference range governance,
  LOINC unit conversion, CTCAE v5.0 grading, and Grade 3/4 AE auto-drafts.
- **CEC Endpoint Adjudication Suite** (`v2.0.0`, `In Progress`) — Designer
  Service (Neo4j): Endpoint diagnostic charter criteria. Execution Engine
  (PostgreSQL): Automated de-identified patient event dossiers, 2+1 independent
  reader workflows, tie-breaker escalations, and CDISC SDTM CE export.
- **Aggregate Safety Compiler (DSUR)** (`v2.0.0`, `In Progress`) — Designer
  Service (Neo4j): Reference Safety Information (RSI) models. Execution Engine
  (PostgreSQL): Point-in-time immutable safety snapshots, patient-years exposure
  calculations, MedDRA SOC/PT cross-tabulations, and QPPV e-signatures.
- **Medical Device / IVD Suite** (`v2.0.0`, `In Progress`) — Designer Service
  (Neo4j): Device component definitions. Execution Engine (PostgreSQL): UDI
  barcode scanning, serialized inventory tracking, device deficiency eCRFs, and
  automated RTSM quarantine / ADE safety workflows.
- **Participant Financial Wallet** (`v2.0.0`, `In Progress`) — Designer Service
  (Neo4j): SoA-linked participant compensation schedules. Execution Engine
  (PostgreSQL): EDC visit completion credit triggers, mobile receipt OCR expense
  claims, digital card/ACH disbursements, and EU CTR 536/2014 Lay Summaries.
- **Reactive Clinical Event Spine** (`v2.0.0`, `In Progress`) — Designer Service
  (Neo4j): Typed clinical event schemas. Execution Engine (PostgreSQL):
  Transactional outbox relay, idempotent event consumers, and zero-data-loss
  cross-app reactive synchronization.

---

## 2. Clinical Entities Mapping

The list below specifies how individual clinical domain entities are processed,
logged, and persisted by their corresponding sub-systems and listeners.

- **Study Protocols** (`Designer`, `Neo4j`): Graph Node Versioning.
- **Epochs** (`Designer`, `Neo4j`): Graph Path Branching.
- **Visits** (`Designer`, `Neo4j`): Graph Node Versioning.
- **Subjects** (`Execution`, `PostgreSQL`): App-Layer Event Interceptor.
- **eCRF Form Submissions** (`Execution`, `PostgreSQL`): App-Layer Event
  Interceptor.
- **System Audit Logs** (`Execution`, `PostgreSQL`): Background Cryptographic
  Sealer & DB Triggers.
- **TMF Documents** (`eTMF Service`, `SQLite/PostgreSQL`): Ingestion-driven
  validation, QC transition logging, and TMFAuditLog ledger.
- **eISF Documents** (`eISF Service`, `SQLite/PostgreSQL`): Ingestion-driven
  validation, CRUD operations, site-isolation gating, and ISFAuditLog ledger.
- **eISF Audit Logs** (`eISF Service`, `SQLite/PostgreSQL`): Append-only
  chronological audit logging for all site operations, views, and sync conflict
  resolutions.
- **CTMS Visits & Milestones** (`CTMS Service`, `SQLite/PostgreSQL`): Explicit
  `CTMSAuditLog` writes & standard Part 11 fields.
- **Protocol Deviations & CAPA** (`Quality Service`, `SQLite/PostgreSQL`):
  Automated `QualityAuditLog` logging, transition controls, and 21 CFR Part 11
  fields.
- **SDTM/ADaM Export** (`Execution`, `PostgreSQL`): `BiostatExport`
  audited-model logging (plus generic `before_flush` hook).
- **Subject Assignments** (`Subject Portal`, `IndexedDB / Client-side Storage`):
  N/A (Client-side execution cached via Service Workers).
- **ePRO Submissions** (`Subject Portal`, `IndexedDB / Client-side Storage`):
  N/A (Client-side execution cached via Service Workers).
- **EHR FHIR Resources** (`Interop Service`, `SQLite/PostgreSQL / interop.db`):
  Explicit `InteropAuditLog` writes with Part 11 metadata.
- **Safety Cases** (`Clinical Safety`, `SQLite/PostgreSQL / safety.db`):
  Explicit `SafetyAuditLog` writes & standard Part 11 fields.
- **Safety Export Jobs** (`Clinical Safety`, `SQLite/PostgreSQL / safety.db`):
  Explicit `SafetyAuditLog` writes & standard Part 11 fields.
- **Safety Reconciliation Runs** (`Clinical Safety`,
  `SQLite/PostgreSQL / safety.db`): Explicit `SafetyAuditLog` writes & standard
  Part 11 fields.
- **Consent Documents** (`Electronic Consent`,
  `SQLite/PostgreSQL / econsent.db`): Explicit `ConsentAuditLog` writes &
  standard Part 11 fields.
- **Consent Signatures** (`Electronic Consent`,
  `SQLite/PostgreSQL / econsent.db`): Explicit `ConsentAuditLog` writes &
  standard Part 11 fields.
- **Archival Delivery Systems** (`Electronic Consent`,
  `SQLite/PostgreSQL / econsent.db`): Explicit `ConsentAuditLog` writes &
  standard Part 11 fields.
- **Patient Notifications** (`Notifications Service`,
  `SQLite/PostgreSQL / notifications.db`): Append-only delivery logs and read
  acknowledgment timestamp auditing.
- **Multi-Channel Alerts** (`Notifications Service`,
  `SQLite/PostgreSQL / notifications.db`): Append-only delivery logs and read
  acknowledgment timestamp auditing.
- **Clinical Coding Assignments** (`Execution`, `SQLite/PostgreSQL`): Automated
  event-driven coding assignments, manual review overrides, system coding query
  triggers, and version up-versioning ledgers.
- **Terminology Concept Codes** (`Designer`, `Neo4j / In-Memory Cache`):
  Stateless search, code verification, and design-time metadata validation (NCI
  Thesaurus/CDISC CT) with signed headers.
- **Biostatistical Exports** (`Execution`, `SQLite/PostgreSQL`): Audit-logged
  `BiostatExport` transactions with Dataset-JSON validation.
- **Global Library Objects** (`Designer`, `Neo4j/Mock DB`): Graph node
  versioning via `PREVIOUS_VERSION` chains and metadata JSON serialization.
- **Study Library Instances** (`Designer`, `Neo4j/Mock DB`):
  Copy-on-instantiation clones linked via `INSTANTIATED_FROM` with local
  overrides.
- **Signature Manifestations** (`Designer & eTMF Services`,
  `Neo4j & SQLite/PostgreSQL`): On-the-fly transient RSA/X.509 cryptographic
  signing of canonical JSON payloads.
- **Organizations** (`Organization Service`, `PostgreSQL/SQLite`): Explicit
  `OrgAuditLog` writes with Part 11 metadata.
- **Clinical Sites** (`Organization Service`, `PostgreSQL/SQLite`): Explicit
  `OrgAuditLog` writes with Part 11 metadata.
- **Personnel** (`Organization Service`, `PostgreSQL/SQLite`): Explicit
  `OrgAuditLog` writes with Part 11 metadata.
- **Delegation of Authority** (`Organization Service`, `PostgreSQL/SQLite`):
  Explicit `OrgAuditLog` writes with Part 11 metadata & dual eISF/eTMF archival
  handoff.
- **Tickets & Comments** (`Tickets Service`, `SQLite/PostgreSQL`): Explicit
  `TicketAuditLog` append-only write pattern and 21 CFR Part 11 fields.
- **File Records & Transfers**
  (`FileShare (Secure File Share & Object Storage)`,
  `SQLite/PostgreSQL / fileshare.db & MinIO/S3`): Explicit `FileshareAuditLog`
  writes with Part 11 metadata & StoragePort SHA-256 integrity validation.
- **Share Grants & Guest Links** (`FileShare Service`,
  `SQLite/PostgreSQL / fileshare.db`): Explicit `FileshareAuditLog` writes with
  Part 11 metadata & cryptographic token verification.
- **Greenlight Site Approvals** (`CTMS / SSU`, `PostgreSQL`): Explicit
  `GreenlightAuditLog` writes with Part 11 signature manifestations & HMAC
  tokens.
- **eSource Ingested Records** (`Interop Service`, `PostgreSQL`): Explicit
  `EhrSourceAuditLog` writes with FHIR provenance IDs & CRC acceptance
  signatures.
- **Laboratory Results & Ranges** (`Execution / Labs`, `PostgreSQL`): Explicit
  `LabAuditLog` writes with LOINC mappings, CTCAE grade flags & auto-draft AE
  linkage.
- **Adjudication Dossiers & Votes** (`Adjudication Service`, `PostgreSQL`):
  Explicit `AdjudicationAuditLog` writes with blinded 2+1 voting ledgers & SDTM
  CE exports.
- **Aggregate Safety Snapshots** (`Clinical Safety`, `PostgreSQL`): Explicit
  `AggregateSafetyAuditLog` writes with immutable dataset hashes & QPPV
  e-signatures.
- **Medical Device Inventory** (`Execution / RTSM`, `PostgreSQL`): Explicit
  `DeviceAuditLog` writes with UDI scans, malfunction reports & RTSM quarantine
  triggers.
- **Participant Stipends & Claims** (`Subject Portal / Payments`, `PostgreSQL`):
  Explicit `PatientPaymentAuditLog` writes with SoA credit triggers, receipt OCR
  hashes & ACH disbursements.
- **Transactional Outbox Events** (`Core Platform Spine`, `PostgreSQL`):
  Append-only `transactional_outbox` with consumer idempotency deduplication
  ledgers.

_(Note: The CRF Builder does not introduce a new persisted database entity, but
instead fully reuses the existing `eCRF Form Submissions` entity)._

---

## 3. Status Indicators & Legend

The functional maturity of the capabilities detailed in the matrix uses the
following indicators:

- **`Supported`**: Feature is fully implemented, verified, and adheres to
  regulatory standards (e.g., 21 CFR Part 11).
- **`In Progress`**: Feature is currently under active development and being
  evaluated in pre-production.
- **`Planned`**: Feature is part of the strategic roadmap but not yet
  implemented.

## 4. Version References

- **Minimum API Version:** The earliest REST/GraphQL API version that supports
  the integration of the corresponding feature.
- **Minimum Engine Version:**
  - `PostgreSQL`: 14.0+ (required for advanced JSONB audit fields and efficient
    trigger execution).
  - `Neo4j`: 5.0+ (required for performant graph traversal of immutable
    versions).
