# ADR-2216: Deepen CDISC Study Submission Compiler

- **Status:** Accepted
- **Date:** 2026-08-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

* **Requirements:** PRD-CDR-002, PRD-CDR-003, ADR-2199, 21 CFR Part 11

---

## 1. Context & Problem Statement

Prior to this architecture refinement, callers across the interoperability microservice (`apps/interop/application/cdr_pipeline_service.py`, `apps/interop/presentation/routers/cdr_pipeline.py`, and `apps/interop/presentation/routers/devices.py`) had to manually coordinate four sequential transformation and compilation stages:

1. Transforming raw canonical observations into 10 core CDISC SDTM domains (`DM`, `AE`, `CM`, `VS`, `LB`, `EX`, `DS`, `TU`, `TR`, `RS`) and domain-specific extensions.
2. Executing automated biostatistical derivation rules for CDISC ADaM datasets (`ADSL`, `ADAE`, `ADTTE`, `BDS`).
3. Applying role-based blinding masks to sensitive treatment arms (`ARM`, `ARMCD`, `ACTARM`, `ACTARMCD`, `EXTRT`, `TRT01P`, `TRT01A`).
4. Serializing datasets into CDISC Dataset-JSON 1.1/2.0 with referential validation and auto-compiling CDISC Define-XML 2.1 specifications with Value Level Metadata (VLM), WhereClauses, Origins, and Method definitions.

This shallow abstraction increased caller coupling, duplicated orchestration logic, and scattered cryptographic sealing operations across service and presentation layers.

## 2. Decision Drivers & Constraints

- **Deep Module Principles:** As established in codebase-design principles, modules should have simple, compact interfaces while hiding substantial operational complexity underneath.
- **Hermetic Package Sealing (21 CFR Part 11):** CDISC submission packages must be deterministically verifiable with unified SHA-256 cryptographic digests across both tabulated/analysis datasets and metadata definitions.
- **Backward Compatibility:** All existing REST API endpoints, DTO contracts, and test assertions in `apps/interop` must maintain 100% backward compatibility.
- **Extensibility:** Device deficiencies (`DD`) and device tracking (`DO`) must seamlessly integrate into the submission compiler.

## 3. Options Considered

### Option 1: **Unified Deep Submission Engine (`CdiscSubmissionEngine` with `SubmissionPackage` Entity) [Selected]:**

- **Overview:** Create a cohesive domain engine in `apps/interop/domain/cdr/submission_engine.py` encapsulating the complete 4-step pipeline behind a clean, single-call `compile_submission_package(...)` interface returning a sealed `SubmissionPackage` model.
- _**Pros:**_
  - ✅ Satisfies architectural requirements
- _**Cons:**_
  - ❌ Requires implementation and maintenance overhead

### Option 2: **Leave Pipeline Logic Distributed in Application Service:**

- **Overview:** Keep individual compiler engines decoupled and orchestrate them within `CDRPipelineService`. (Rejected: leads to shallow interfaces, leakage of step sequence details, and redundant boilerplate in routers).
- _**Pros:**_
  - ✅ Satisfies architectural requirements
- _**Cons:**_
  - ❌ Requires implementation and maintenance overhead

## 4. Decision Outcome

- **Chosen Option:** **Option 1**.
- **Justification:** Selected as primary architectural approach.
  We introduced `CdiscSubmissionEngine` and `SubmissionPackage` in `apps/interop/domain/cdr/submission_engine.py`.

Key elements of the design:

- **`SubmissionPackage` Domain Entity:** An immutable, typed domain model containing SDTM domains, ADaM datasets, Dataset-JSON document and UTF-8 string, Define-XML 2.1 string and validation outcome, record counts, and canonical SHA-256 digital seal.
- **Single-Call Compiler Method:** `compile_submission_package(...)` executes SDTM mapping, ADaM derivations, blinding masks, Dataset-JSON serialization, and Define-XML compilation in a single invocation.
- **Dedicated Sub-Stage Facades:** `compile_sdtm`, `compile_adam`, `compile_dataset_json`, `compile_define_xml`, `compile_device_sdtm`, and `mask_blinding` are exposed on `CdiscSubmissionEngine` for targeted usage.
- **Service & Router Delegation:** `CDRPipelineService`, `cdr_pipeline.py`, `cdr_lakehouse.py`, and `devices.py` all delegate directly to `CdiscSubmissionEngine`.

## 5. Consequences & Trade-offs

- **Positive:**
  - High leverage and deep encapsulation: callers interact with a single, clear interface.
  - Consistent GxP auditability and cryptographic integrity across all export and compilation flows.
  - Reusability across human clinical trials and medical device investigation exports.
- **Negative:**
  - Minor indirection layer introduced in `apps/interop/domain/cdr/submission_engine.py`.

## 6. Implementation & Verification

- **Domain Module:** `apps/interop/domain/cdr/submission_engine.py`
- **Domain Re-exports:** `apps/interop/domain/cdr/__init__.py`
- **Application Service:** `apps/interop/application/cdr_pipeline_service.py`
- **Routers:** `apps/interop/presentation/routers/cdr_pipeline.py`, `apps/interop/presentation/routers/cdr_lakehouse.py`, `apps/interop/presentation/routers/devices.py`
- **Automated Tests:** Comprehensive unit and integration test suite `TestCdiscSubmissionEngine` in `apps/interop/tests/test_cdr_cdisc_sdtm_adam_pipeline.py` and `test_device_sdtm_regulatory_exports.py`.
