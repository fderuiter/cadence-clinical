# ADR-2194: Asynchronous Protocol Digitization Stage DAG with USDM Compilation

- **Status:** Accepted
- **Date:** 2026-08-21
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Clinical protocol ingestion requires extracting complex multi-domain structures from unstructured PDF/DOCX files, including study design metadata, Schedule of Activities (SoA) timelines, biomedical concepts, eligibility criteria with logical expressions, and CDASH-compliant eCRFs. Monolithic extraction pipelines suffer from opaque failure modes, lack of intermediate checkpointing, and cannot validate schema invariants at domain boundaries before proceeding to downstream transformations.

This decision addresses requirement **PRD-DDF-001** (AI-Native USDM Protocol Digitization & Automated eCRF/SoA Synthesis Engine), **PRD-SYS-001** (21 CFR Part 11 Audit Attribution), and **PRD-MDR-007** (Clinical Metadata Repository & Graph Ingestion).

## 2. Decision Drivers & Constraints

- **Resilience & Checkpointing:** Long-running protocol extraction workflows must persist immutable intermediate stage results, enabling progress inspection and resumption from failed/paused checkpoints.
- **Strict Schema Validation Gates:** Each stage boundary must validate output payloads against strict Pydantic v2 schemas before permitting subsequent downstream stages to execute.
- **Asynchronous Execution & Non-blocking SLAs:** File ingestion and multi-stage NLP transformations must run asynchronously in background tasks while exposing real-time polling and stage progress endpoints (`/api/v1/designer/digitization/dag/*`).
- **CDISC USDM v4.0 & CDASH Compliance:** Compiled outputs must produce valid CDISC USDM v4.0 graph representations and CDASH-compliant eCRF forms with circular edit check detection.
- **21 CFR Part 11 Audit Attribution:** Committing compiled USDM models to the Neo4j knowledge graph requires mandatory change justification reasons and user identity attribution.

## 3. Options Considered

### Option 1: Monolithic Synchronous Extraction Pipeline
- **Overview:** Maintain a single-shot synchronous endpoint that runs all extraction and compilation logic in one request cycle.
- **Pros:** Simple API contract; minimal intermediate state storage.
- **Cons:** ❌ Timeouts on large protocol documents (>50 pages); no granular progress reporting; failure at any step requires restarting the entire extraction; no checkpoint inspection.

### Option 2: Multi-Stage Directed Acyclic Graph (DAG) with Checkpointing & Schema Gates (Selected)
- **Overview:** Decompose protocol ingestion into five discrete, sequenced stages: `LAYOUT_PARSING` ➔ `SOA_EXTRACTION` ➔ `BIOMEDICAL_CONCEPT_MAPPING` ➔ `ECRF_SYNTHESIS` ➔ `USDM_COMPILATION`. Each stage executes against a thread-safe job store, records timing/confidence metrics, and validates outputs through strict schema gates before advancing.
- **Pros:**
  - ✅ Resilient execution with immutable stage checkpoints and resumption.
  - ✅ Clear fault localization and diagnostic error capture at schema validation gates.
  - ✅ Non-blocking asynchronous job execution with real-time progress percentages.
  - ✅ Clean separation of concerns matching CDISC MDR/DDF domain models.
- **Cons:**
  - ❌ Requires in-memory / persistent job state management.

## 4. Decision Outcome

- **Chosen Option:** Option 2 (Multi-Stage Directed Acyclic Graph with Checkpointing & Schema Gates).
- **Justification:** Satisfies PRD-DDF-001 and PRD-SYS-001 by providing robust, checkpointed, and audited protocol ingestion while maintaining backward compatibility with existing synchronous endpoints.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - Complete visibility into stage-by-stage extraction progress.
  - Diagnostic error traces isolate exact schema validation failures (e.g. invalid domain code or cyclic rule).
  - Checkpoints enable sandboxed inspection and incremental manual corrections before final Neo4j commit.
- **Negative Impact / Trade-offs:**
  - Additional domain models and state store management in `apps/designer`.
- **Mitigation Strategy:**
  - Provide in-memory thread-safe `DigitizationJobStore` with memory cleanup and query pagination.

## 6. Implementation & Verification

- **Affected Services & Files:**
  - `apps/designer/domain/digitization_dag_models.py`
  - `apps/designer/adapters/digitization_job_store.py`
  - `apps/designer/application/services/digitization_dag_service.py`
  - `apps/designer/presentation/routers/digitization.py`
- **Verification Plan:**
  - Unit and integration tests in `apps/designer/tests/test_digitization_stage_dag.py`.
  - Verification of schema validation gate rejection on corrupted intermediate data.
  - Verification of stage resumption and Part 11 commit attribution.
  - Automated GxP synchronization via `uv run python scripts/sync_gxp.py`.
