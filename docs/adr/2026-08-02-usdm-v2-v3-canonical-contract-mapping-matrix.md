# ADR-053: USDM v2/v3 Canonical Contract and Mapping Coverage Matrix

* **Status:** Accepted
* **Date:** 2026-08-02
* **Authors:** @jules
* **Deciders:** @fderuiter, @jules

---

## 1. Context & Problem Statement
In accordance with GxP validation pipelines and the 21 CFR Part 11 electronic records regulations, the Cadence Clinical platform requires absolute, reproducible, and verifiable transformations of clinical trial definitions. The system unifies clinical study setup via the MDR Designer (with a Neo4j graph back-end) and clinical trial data capture via the EDC Execution engine (with a PostgreSQL relational store).

To support the full import, export, and round-trip of study designs using the Clinical Data Interchange Standards Consortium (CDISC) Unified Study Definitions Model (USDM) versions v2 and v3, we must define a single, stable **Canonical Internal Contract** and a **Mapping Coverage Matrix**. This documented contract is a strict GxP requirement before implementing normalization pipelines and bidirectional mappers. Without a formal field-level specification, lossless vs. lossy mapping behaviors are non-deterministic, potentially introducing regulatory risks, silent data truncation, or audit inconsistencies.

This decision implements requirements under PRD-MDR-001.

## 2. Decision Drivers & Constraints
* **Driver 1 (Compliance & Verifiability):** FDA 21 CFR Part 11 and EU Annex 11 mandate precise data trace-to-source trails. Any schema transformation or dynamic mapping must have deterministic, predictable outcomes.
* **Driver 2 (Lossless Round-Trip):** Elements designed in the MDR must be translatable into the USDM exchange model and importable into the EDC Execution engine with zero loss of critical clinical or metadata parameters (Material Fidelity).
* **Driver 3 (Performance & Latency):** Schema resolution, including terminology concept lookups, must satisfy the 200ms API SLA. This requires the contract to integrate cleanly with our single-projection database retrievals and Terminology Cache.
* **Driver 4 (Unsupported Construct Explicitness):** Non-standard structures or logic configurations that cannot be safely translated into standard clinical models must be caught, rejected, and logged with precise, user-visible error payloads.

## 3. Options Considered

### Option 1: Direct Loose Mapping (Schema-less Conversion)
Map incoming USDM v2/v3 structures directly to relational models dynamically using key-value JSON parsing and standard python dict manipulation, without defining a central canonical intermediary contract.
* **Pros:**
  * ✅ High developer velocity and lower initial specification complexity.
  * ✅ Less rigid; handles unanticipated fields by dumping them into generic JSON columns.
* **Cons:**
  * ❌ Silent truncation or loss of structural relationships occurs without compile-time checks, leading to severe regulatory compliance and data integrity hazards.
  * ❌ Unpredictable behavior across study updates makes it impossible to guarantee reproducible behavior under 21 CFR § 11.10(a).

### Option 2: Rigid Multi-Version Direct Adapters (v2-to-EDC & v3-to-EDC)
Create entirely separate, independent translation mappers for every incoming version (USDM v2 and USDM v3) to and from the internal graph/relational database schemas.
* **Pros:**
  * ✅ Highly optimized converters for specific target structures.
* **Cons:**
  * ❌ Maintenance overhead scales quadratically as new USDM versions (e.g., v4) are introduced.
  * ❌ Lack of a central canonical contract prevents universal round-trip validations, making bidirectional fidelity checks brittle.

### Option 3: Unified Intermediary Canonical Contract (Selected)
Define a stable, version-controlled, and audited intermediate schema (the **Canonical Internal Contract**) as the universal representation. Implement mappers from USDM v2 and USDM v3 into this Canonical Shape, and serialize from this Canonical Shape back to compliant USDM structures.
* **Pros:**
  * ✅ Ensures highly deterministic, reproducible schema conversions.
  * ✅ Isolates the backend database layers from changing external CDISC standards. Adding a new USDM version only requires a single new mapper (Version -> Canonical) rather than rewriting core execution engines.
  * ✅ Enables clear isolation of Material vs. Non-Material fidelity differences, providing stable audit pathways and automated validation reports.
* **Cons:**
  * ❌ Requires upfront effort to document the extensive field-level mapping and build intermediate validation checks.

## 4. Decision Outcome

* **Chosen Option:** Option 3 (Unified Intermediary Canonical Contract)
* **Justification:** Implementing a central, audited canonical contract is the only way to satisfy GxP data integrity and 21 CFR Part 11 compliance. It decouples schema-level evolutions, eliminates silent truncation risks, and allows the implementation of deterministic, automated verification tests.

---

### Architectural & Field-Level Specifications

#### A. Canonical Schema Contract Specification
The Canonical Shape defines the stable representation of clinical study designs inside the Cadence Clinical platform. All internal operations (such as diffing, rules validation, and database synchronization) operate exclusively on this format.

```json
{
  "study_id": "string (UUID or stable unique ID)",
  "title": "string",
  "current_version": "string (semantic version)",
  "desc": "string (optional)",
  "sponsor_id": "string",
  "status": "string (DRAFT | LOCKED | APPROVED | ARCHIVED)",
  "therapeutic_area": "string (optional)",
  "arms": [
    {
      "arm_id": "string",
      "name": "string",
      "arm_type_concept_id": "string (concept lookup key)",
      "target_sample_size": "integer",
      "randomization_ratio": "string",
      "visits": [
        {
          "visit_id": "string",
          "name": "string",
          "visit_type_concept_id": "string",
          "planned_day": "integer",
          "window_days": "integer",
          "activities": [
            {
              "activity_id": "string",
              "name": "string",
              "form_oid": "string (optional)",
              "rules": [
                {
                  "id": "string",
                  "type": "string (skip_logic | constraint)",
                  "condition": {
                    "type": "string (comparison | logic_gate | field_ref | constant)",
                    "operator": "string (== | != | < | > | <= | >= | AND | OR)",
                    "operands": "array (nested condition structures)"
                  },
                  "action": "string (hide | show | disable | enable, optional)",
                  "query_message": "string (optional)",
                  "version_index": "integer"
                }
              ]
            }
          ]
        }
      ]
    }
  ],
  "rules": [
    {
      "id": "string",
      "type": "string",
      "condition": "object",
      "action": "string",
      "target_field": "string",
      "target_form": "string (optional)",
      "target_group": "string (optional)",
      "query_message": "string (optional)",
      "version_index": "integer"
    }
  ],
  "audit_metadata": {
    "created_at": "string (ISO 8601 UTC timestamp)",
    "created_by": "string (OIDC User Subject UUID)",
    "reason_for_change": "string (non-empty audit comment)",
    "version_index": "integer"
  }
}
```

#### B. USDM v2/v3 to Canonical Mapping Matrix
The table below specifies the mapping rules and preservation behavior between CDISC USDM v2/v3 entities and the Cadence Canonical Shape.

| USDM Entity & Field (v2/v3) | Canonical Target Field | Mapping Strategy | Preservation Behavior & GxP Rationale |
| :--- | :--- | :--- | :--- |
| `Study.id` | `study_id` | Direct | **Lossless.** Enforces absolute physical identity. |
| `Study.name` / `StudyTitle.text` | `title` | Direct | **Lossless.** Preserves trial identification metrics. |
| `StudyVersion.id` | `version_id` (Internal) | Direct | **Lossless.** Tracks GxP release state isolation. |
| `StudyVersion.version` | `current_version` | Direct | **Lossless.** Tracks version index increments. |
| `StudyDesign.id` | `design_id` | Direct | **Lossless.** Identifies primary study execution schema. |
| `StudyArm.id` | `arm_id` | Direct | **Lossless.** Preserves clinical subject grouping IDs. |
| `StudyArm.name` | `name` | Direct | **Lossless.** Preserves user-facing naming. |
| `StudyArm.armType` (Code) | `arm_type_concept_id` | Resolved Lookup | **Normalized.** Maps complex USDM code blocks to cache-resolved, single-row concepts. |
| `StudyEpoch.id` | `epoch_id` | Direct | **Lossless.** Retains temporal trial phase isolation. |
| `Encounter.id` (representing Visits) | `visit_id` | Direct | **Lossless.** Retains visit sequence reference. |
| `Encounter.name` | `name` | Direct | **Lossless.** Retains UI title of the clinical visit. |
| `Encounter.type` (Code) | `visit_type_concept_id` | Resolved Lookup | **Normalized.** Maps complex OIDC/USDM coding arrays to single concept entries via Cache. |
| `Activity.id` | `activity_id` | Direct | **Lossless.** Preserves clinical action tracking boundaries. |
| `Activity.name` | `name` | Direct | **Lossless.** Retains the activity description. |
| `TransitionRule.id` | `rules[i].id` | Structured Cast | **Normalized.** Converts rule expressions to Abstract Syntax Tree (AST) representations. |
| `AuditFields.changeReason` | `audit_metadata.reason_for_change` | Direct | **Lossless.** Enforces 21 CFR § 11.10(e) logging. |

#### C. Supported, Normalized, and Unsupported Constructs

##### 1. Supported Constructs
* **Full Study Hierarchy:** Direct, nested mappings of Study $\rightarrow$ StudyVersion $\rightarrow$ StudyDesign $\rightarrow$ StudyArm $\rightarrow$ StudyEpoch $\rightarrow$ Visit (Encounter) $\rightarrow$ Activity.
* **Biomedical Concept Terminology:** Standard vocabulary mappings (such as SNOMED-CT, MedDRA, LOINC) backed by the fast, thread-safe Terminology Cache.
* **Standard Rules Expressions:** Skip logic, constraint checks, and cross-form clinical edit checks using standard relational comparison operators (`==`, `!=`, `<`, `>`, `<=`, `>=`) and logical gates (`AND`, `OR`).
* **Compliance Audit Fields:** Full capture of GxP parameters (`created_at`, `created_by`, `reason_for_change`, `version_index`) across every structural mutation.

##### 2. Normalized Constructs
* **String Identifier Casts:** Standardizes varying external identifier structures to secure, system-wide UUIDs.
* **Terminology System Normalization:** Case-insensitive standardization of medical vocabularies (e.g. converting `SnomedCT`, `SNOMED_CT`, and `SNOMED-CT` to normalized `SNOMED-CT`).
* **Duration/Timing Formats:** Normalizes timing window offsets into ISO 8601 duration format standardizations (e.g., `P3D` for 3 planned days).

##### 3. Explicitly Unsupported Constructs (Rejected at Ingestion Layer)
* **Infinite Recurrent Branching:** The rules engine strictly rejects cyclical skip-logic paths using our three-color DFS cycle detector, returning an HTTP 422 error.
* **Custom Extensible Elements:** Non-standard XML or JSON tags that do not map to the USDM v2/v3 structure are dropped, with warning messages generated in the compliance logs.
* **Stochastic/Complex Math Operators:** Mathematical operators beyond standard arithmetic (e.g., custom trigonometric or statistical functions) are unsupported and will fail validation during rule compilation preview.

#### D. Material vs. Non-Material Fidelity Differences

To enable robust, reproducible auditing, we categorize differences during round-trips into two distinct tiers:

##### 1. Material Fidelity Differences (Strict GxP Infractions)
These differences constitute a loss of data integrity or compliance, causing the system to automatically fail the import job and raise high-priority compliance exceptions:
* **Identification Leaks or Loss:** Any change, truncation, or drop of `study_id`, `version_id`, `arm_id`, `visit_id`, or `activity_id`.
* **Audit Trail Loss:** Dropping, modifying, or failing to supply the `reason_for_change` or executing user ID.
* **Constraint Boundaries Modification:** Alteration of a rule's logical expression (such as converting `age < 18` to `age <= 18`), which would violate protocol integrity.
* **Terminology Code Mismatch:** Truncation or mismatch of standardized terminology codes (e.g., converting a LOINC laboratory code from `2823-3` to a generic uncoded string).

##### 2. Non-Material Fidelity Differences (Permitted Representation Changes)
These represent stylistic or formatting differences that do not compromise data integrity or regulatory compliance:
* **Whitespace & Carriage Return Normalization:** Whitespace spacing, indentation, and trailing newlines in JSON or XML payloads.
* **Key Sorting Order:** Since our signatures are computed using key-sorted, canonicalized JSON representations, the arbitrary order of JSON object keys is treated as non-material.
* **Localization Label Fallbacks:** Displaying standard fallback labels (e.g., displaying the English term "Systolic blood pressure" if a Spanish translation label is missing).
* **Trailing Zeros in Minor Versions:** Minor version string representations (such as treating semantic versions `1.1` and `1.1.0` as functionally identical).

#### E. Stable Path Naming for Reports
All validation, difference, and audit logs are persisted using predictable, stable path naming structures to allow automated CI/CD parsing and easy auditor discovery:
* **Alignment Validation Report:** `docs/compliance/reports/alignment_validation_{study_id}.json`
* **Study Difference Report:** `docs/compliance/reports/study_diff_{study_id}_{action_id1}_{action_id2}.json`
* **GxP Import Audit Log:** `docs/compliance/reports/gxp_import_audit_{study_id}.json`

## 5. Consequences & Trade-offs

* **Positive Impact:**
  * ✅ Enforces GxP validation compliance with robust, clear, and deterministic mapping boundaries.
  * ✅ Eliminates dynamic truncation risks, ensuring consistent audit trail serialization.
  * ✅ Dramatically simplifies the integration of subsequent USDM versions (e.g. USDM v4).
* **Negative Impact / Technical Debt:**
  * ❌ Increases upfront overhead during the ingestion of external payloads since every field must be mapped and validated against the Canonical Shape.
  * ❌ Requires maintaining mapping utilities and validation rules in both Python (backend) and potential future typescript (frontend) pipelines.

## 6. Implementation & Verification

* **Affected Repositories / Services:** `apps/designer/mapper.py`, `apps/designer/validator.py`
* **Verification Plan:**
  1. **Compliance Validation:** Run `scripts/validate_adrs.py` to ensure that this ADR complies with GxP indices.
  2. **Unit Tests:** Execute existing mapper and validator test suites using `uv run pytest tests/test_transformers.py tests/test_validator.py` to verify that schema conversions remain accurate and execution times stay under 200ms.
