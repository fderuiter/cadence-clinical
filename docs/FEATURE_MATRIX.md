# Feature & Compatibility Matrix

## 1. Feature Categories & Environment Matrix
This matrix details the distribution of core compliance and tracking features across the primary services in the Cadence Clinical architecture.

| Feature / Capability | Designer Service (Neo4j) | Execution Engine (PostgreSQL) | Minimum API / Engine Version | Status |
| :--- | :--- | :--- | :--- | :--- |
| **Metadata Tracking** | Centralized CDISC USDM management | Local execution state tracking | v1.0.0 | Supported |
| **Graph Versioning** | Full historical graph paths | N/A (Relational state) | v1.2.0 | Supported |
| **Cryptographic Sealing** | Chained audit node creation | Background chained hashing | v1.1.0 | Supported |
| **Hard-Delete Capture** | Soft deletes via relationships | Database-level trigger / shadow schema | v1.0.0 | Supported |
| **Masked CSV Export** | Structural hashing of authors | Dynamic PII masking & hashing | v1.3.0 | Supported |
| **21 CFR Part 11 Fields**| Enforced on all mutations | Enforced on all transactions | v1.0.0 | Supported |
| **eTMF Taxonomy Classification** | API Gateway resolution via catalog | Enforced strict ingestion hierarchy validations | v1.4.0 | Supported |
| **CTMS Site Operational Tracking**| N/A | Secured monitor/milestone/CRA workload operations & append-only audit trail | v1.5.0 | Supported |
| **Quality & CAPA Management**     | N/A | Secured protocol deviation tracking, Root Cause Analysis (RCA), CAPA transition workflows, and append-only QA audit logs | v1.5.0 | Supported |
| **ePRO Patient Diary Submissions** | N/A | Secured REST single/bulk submissions with deterministic conflict strategy resolution | v1.6.0 | Supported |
| **Offline PWA Portal & Sync**     | Independent client UI and IndexedDB queue | N/A (Client-side execution cached via Service Workers) | v1.6.0 | Supported |
| **Multi-Channel Patient Alerts**  | N/A | Automated compliance reminders computed from Subject Assignments via SMS/Email/Webhook | v1.6.0 | Supported |
| **Medical Coding: AE Coverage**   | N/A | Automated MedDRA dictionary coding and system query generation for Adverse Events (AETERM) | v1.5.0 | Supported |
| **Medical Coding: MH Coverage**   | N/A | Automated MedDRA dictionary coding and system query generation for Medical History (MHTERM) | v1.5.0 | Supported |
| **Medical Coding: CM Coverage**   | N/A | Automated WHODrug dictionary coding and system query generation for Concomitant Medications (CMTRT) | v1.5.0 | Supported |
| **SDTM/ADaM CDISC Export**        | N/A | Secured Dataset-JSON 1.0.0 format exports (DM, AE, VS, LB, MH, CM, ADSL, ADAE, ADVS) with audit trails | v1.7.0 | Supported |
| **Global Library Templates**      | Multi-versioned Forms, Data Elements, Arms, and Visits | N/A (Referenced downstream on instantiation) | v1.8.0 | Supported |
| **Multi-Tenant Scoping**          | Metadata partitioned by validated sponsor IDs, blank context blocked | N/A | v1.8.0 | Supported |
| **Governance & State Machine**    | Allowed transition validations and role gates (DRAFT to ARCHIVED) | N/A | v1.8.0 | Supported |
| **In-Use Locks & Amendments**     | In-use template mutation write blocking, formal `/amend` cloning workflow | N/A | v1.8.0 | Supported |
| **Native Part 11 eSignatures**    | Certificate-bound protocol-approval signing & graph version locking | Certificate-bound document-signing with 60s gateway step-up token, replay prevention & immutability locking | v1.9.0 | Supported |
| **Tickets & Query Escalation**    | N/A | Secured query/ticket tracking including comments, status transitions, optimistic locking, and background SLA escalation with GxP audit logs | v1.9.0 | Supported |

---

## 2. Clinical Entities Mapping
The table below specifies how individual clinical domain entities are processed, logged, and persisted by their corresponding sub-systems and listeners.

| Clinical Entity | Sub-system | Persistence Backend | Audit Listener Pattern |
| :--- | :--- | :--- | :--- |
| **Study Protocols** | Designer | Neo4j | Graph Node Versioning |
| **Epochs** | Designer | Neo4j | Graph Path Branching |
| **Visits** | Designer | Neo4j | Graph Node Versioning |
| **Subjects** | Execution | PostgreSQL | App-Layer Event Interceptor |
| **eCRF Form Submissions** | Execution | PostgreSQL | App-Layer Event Interceptor |
| **System Audit Logs** | Execution | PostgreSQL | Background Cryptographic Sealer & DB Triggers |
| **TMF Documents** | eTMF Service | SQLite/PostgreSQL | Ingestion-driven validation, QC transition logging, and TMFAuditLog ledger |
| **eISF Documents** | eISF Service | SQLite/PostgreSQL | Ingestion-driven validation, CRUD operations, site-isolation gating, and ISFAuditLog ledger |
| **eISF Audit Logs** | eISF Service | SQLite/PostgreSQL | Append-only chronological audit logging for all site operations, views, and sync conflict resolutions |
| **CTMS Visits & Milestones** | CTMS Service | SQLite/PostgreSQL | Explicit `CTMSAuditLog` writes & standard Part 11 fields |
| **Protocol Deviations & CAPA**| Quality Service | SQLite/PostgreSQL | Automated `QualityAuditLog` logging, transition controls, and 21 CFR Part 11 fields |
| **Subject Assignments** | Interop Service | SQLite/PostgreSQL | Explicit `InteropAuditLog` writes with Part 11 metadata |
| **ePRO Submissions** | Interop Service | SQLite/PostgreSQL | Immutable database-level submission logging and conflict strategy reconciliation |
| **Patient Notifications**| Interop Service | SQLite/PostgreSQL | Append-only delivery logs and read acknowledgment timestamp auditing |
| **Clinical Coding Assignments**| Execution | SQLite/PostgreSQL | Automated event-driven coding assignments, manual review overrides, system coding query triggers, and version up-versioning ledgers |
| **Biostatistical Exports** | Execution | SQLite/PostgreSQL | Audit-logged `BiostatExport` transactions with Dataset-JSON validation |
| **Global Library Objects** | Designer | Neo4j/Mock DB | Graph node versioning via `PREVIOUS_VERSION` chains and metadata JSON serialization |
| **Study Library Instances** | Designer | Neo4j/Mock DB | Copy-on-instantiation clones linked via `INSTANTIATED_FROM` with local overrides |
| **Signature Manifestations** | Designer & eTMF Services | Neo4j & SQLite/PostgreSQL | On-the-fly transient RSA/X.509 cryptographic signing of canonical JSON payloads |
| **Organizations** | Organization Service | PostgreSQL/SQLite | Explicit `OrgAuditLog` writes with Part 11 metadata |
| **Clinical Sites** | Organization Service | PostgreSQL/SQLite | Explicit `OrgAuditLog` writes with Part 11 metadata |
| **Personnel** | Organization Service | PostgreSQL/SQLite | Explicit `OrgAuditLog` writes with Part 11 metadata |
| **Delegation of Authority** | Organization Service | PostgreSQL/SQLite | Explicit `OrgAuditLog` writes with Part 11 metadata & dual eISF/eTMF archival handoff |
| **Tickets & Comments** | Tickets Service | SQLite/PostgreSQL | Explicit `TicketAuditLog` append-only write pattern and 21 CFR Part 11 fields |

---

## 3. Status Indicators & Legend
The functional maturity of the capabilities detailed in the matrix uses the following indicators:

* **`Supported`**: Feature is fully implemented, verified, and adheres to regulatory standards (e.g., 21 CFR Part 11).
* **`In Progress`**: Feature is currently under active development and being evaluated in pre-production.
* **`Planned`**: Feature is part of the strategic roadmap but not yet implemented.

## 4. Version References
* **Minimum API Version:** The earliest REST/GraphQL API version that supports the integration of the corresponding feature.
* **Minimum Engine Version:**
  * `PostgreSQL`: 14.0+ (required for advanced JSONB audit fields and efficient trigger execution).
  * `Neo4j`: 5.0+ (required for performant graph traversal of immutable versions).
