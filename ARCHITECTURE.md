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

### C. Shared Core Models (`packages/core-models`)
* **Role:** Unified Type System.
* **Core Responsibilities:**
  * Pydantic v2 representations of CDISC USDM objects.
  * In-memory bidirectional transformation adapters (USDM JSON ↔ OpenRosa / CDISC ODM).

### D. Gateway & Identity (`apps/gateway`)
* **Role:** Reverse Proxy & Access Control.
* **Core Responsibilities:**
  * Keycloak OIDC JWT validation.
  * Centralized Role-Based Access Control (RBAC) mapping:
    * `Study Designer` ──► Design permissions in `apps/designer`
    * `Site Investigator / CRC` ──► Data capture permissions in `apps/execution`
    * `Data Manager` ──► Query and form management across both domains.

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
 [ Provisioned into PostgreSQL EDC ]
                 │
                 ▼
 [ Live Site Data Entry & Audit Log ]
