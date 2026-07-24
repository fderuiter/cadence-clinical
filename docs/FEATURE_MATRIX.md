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
