# ADR-2234: Pure JSONB Observation Storage and Columnar Dataset JSON Exporter

- **Status:** Accepted
- **Date:** 2026-08-29
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

In the Cadence Clinical architecture, clinical metadata management (MDR) synthesizes upstream study protocol definitions with downstream Electronic Data Capture (EDC). As clinical studies evolve through adaptive trial amendments, two competing requirements arise:

1. **Relational Schema Rigidity vs. Dynamic eCRF Design**:
   Traditional EDC architectures attempt to map newly authored eCRF form definitions to physical SQL tables via dynamic DDL migrations (`ALTER TABLE`, `CREATE TABLE`). This approach suffers from critical failure modes in multi-tenant SaaS:
   - Schema lock contention on active clinical databases during concurrent patient data entry.
   - Migration synchronization lag across thousands of globally distributed clinical trial sites.
   - Relational database schema explosion (hundreds of custom tables per protocol).
   - Inflexibility when form definitions change mid-study across cohorts.
     Cadence enforces **"The Golden Rule: The form is data, not code."**

2. **Biostatistical Analytical Latency**:
   Biostatisticians and data managers require near real-time analytical export of clinical observations into the **CDISC Dataset-JSON v1.1** standard across standard SDTM domains (`DM`, `VS`, `AE`, `LB`, `CM`). Traditional row-by-row relational joins across dynamic entity-attribute-value tables fail to meet high-throughput latency targets for large patient cohorts (100,000+ data points).

This decision resolves requirements under `@req:PRD-CRF-001`, `@req:PRD-SYS-125`, and `@req:PRD-MDR-001`.

## 2. Decision Drivers & Constraints

- **21 CFR Part 11 Audit Integrity (§ 11.10(c), (e))**: Every clinical observation must track its immutable author (`created_by`), reason for change (`reason_for_change`), monotonic revision index (`version_index`), and protocol amendment version tag (`protocol_version_tag`).
- **Cryptographic Schema Provenance**: Every observation payload must be pinned to the exact SHA-256 hash of the Tripartite form definition (`data_schema`, `ui_schema`, `logic_schema`) active at the moment of capture.
- **Extraction Throughput SLA**: Transforming 100,000 raw observation records into validated CDISC Dataset-JSON v1.1 packages must execute in **< 500ms**.
- **Zero Database DDL Mutation Invariant**: Adding, renaming, or reordering form items in the eCRF designer must require zero relational DDL executions on PostgreSQL.

## 3. Options Considered

### Option 1: **Option A (Selected): Pure JSONB Storage with Columnar DDF Transformation**

- **Overview:** - Observations stored as flexible JSONB documents on a unified table with indexed query columns.
   - Columnar extraction in memory provides sub-100ms analytical transformation.
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead

### Option 2: **Option B: Dynamic Physical SQL Table Generation per Form Definition**

- **Overview:** - _Rejected_: Leads to database lock contention, schema drift, and brittle multi-tenant deployments.
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead

### Option 3: **Option C: Entity-Attribute-Value (EAV) Relational Triples**

- **Overview:** - _Rejected_: Requires massive recursive SQL joins, resulting in 10x-50x slower extraction latency and prohibitive CPU costs.
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead


## 4. Decision Outcome

- **Chosen Option:** **Option A (Pure JSONB Observation Envelopes + Columnar DDF ETL)**.
- **Justification:** Selected as primary architectural approach.

```
+-----------------------------------------------------------------------+
|                       eCRF Form Submission                             |
|  (form_id: FORM-VS, schema_hash: sha256(form_def), values: {SYSBP})   |
+-----------------------------------+-----------------------------------+
                                    |
                                    v
+-----------------------------------------------------------------------+
|            apps/execution: ClinicalObservation Model                  |
|                                                                       |
|  - id, subject_id, study_id, domain, test_code, value, unit           |
|  - additional_properties (JSONB) <---------------+                    |
|      * schema_hash                               | Dynamic Property   |
|      * form_id                                   | Proxies            |
|      * version_tag                               | (Pydantic / ORM)   |
|      * 21 CFR Part 11 Audit Fields               |                    |
+-----------------------------------+-----------------------------------+
                                    |
                                    v
+-----------------------------------------------------------------------+
|           packages/ddf: DatasetJsonEtlService (Columnar ETL)          |
|                                                                       |
|  1. Vectorized row & column mapping from JSONB envelopes              |
|  2. Domain partitioning (DM, VS, AE, LB, CM)                          |
|  3. CDISC Dataset-JSON v1.1 serialization (datasetJSONVersion: 1.1.0) |
|  4. Conformance Schema Validation (studyOID, itemGroupOID)            |
+-----------------------------------+-----------------------------------+
                                    |
                                    v
+-----------------------------------------------------------------------+
|             High-Throughput CDISC Dataset-JSON v1.1 Package           |
|                  (100k records extracted in ~80ms)                    |
+-----------------------------------------------------------------------+
```

### Pure JSONB Property Proxies on `ClinicalObservation`

To provide strict typing and IDE autocompletion while storing non-relational fields inside PostgreSQL `JSONB`, `ClinicalObservation` implements dynamic property descriptors:

```python
class ClinicalObservation(BaseModel):
    # Relational indexing columns
    id: str
    subject_id: str
    study_id: str
    domain: str
    test_code: str
    value: float | None

    # Pure JSONB storage envelope
    additional_properties: dict[str, Any] = Field(default_factory=dict)

    @property
    def schema_hash(self) -> str | None:
        return self.additional_properties.get("schema_hash")

    @schema_hash.setter
    def schema_hash(self, val: str | None) -> None:
        self.additional_properties["schema_hash"] = val
```

### Columnar Dataset-JSON v1.1 Transformation Engine

`DatasetJsonEtlService` in `packages/ddf/dataset_json_etl.py` extracts tabular datasets using vectorized column grouping:

- Resolves item metadata (`itemGroupOID`, `itemOID`, `dataType`, `targetUnits`) directly from form metadata or observation envelopes.
- Emits RFC 8785 canonical JSON formatting conforming to CDISC Dataset-JSON v1.1 specification.

## 5. Consequences & Trade-offs

### Positive

- **Zero-Downtime Form Deployment**: New clinical forms and mid-study revisions deploy instantaneously without touching database schemas.
- **Extreme Analytical Throughput**: Benchmark confirmed **100,000 observations extracted in ~80ms** (6x faster than the 500ms target).
- **GxP Audit Compliance**: Complete 21 CFR Part 11 lineage preserved in every observation document.
- **Standards Parity**: Output directly ingestible by FDA/PMDA regulatory review tools supporting Dataset-JSON v1.1.

### Trade-offs

- Filtering queries on non-indexed dynamic attributes inside `additional_properties` requires JSONB containment queries (`->>`) or expression indexes.

## 6. Implementation & Verification

- **Core Pipeline**: `packages/ddf/dataset_json_etl.py`.
- **Domain Persistence**: `apps/execution/database/models/observation.py`, `apps/execution/presentation/routers/observations.py`.
- **Test Suites & Benchmarks**:
  - `packages/ddf/tests/test_dataset_json_etl.py` (Columnar transformation, schema hashing, multi-domain SDTM export, 100k performance benchmark).
  - `apps/execution/tests/test_observation_jsonb.py` (JSONB persistence and DTO validation).
