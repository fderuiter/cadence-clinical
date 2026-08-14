# ADR-2175: Graph-Native Zero-Downtime Protocol Amendment and In-Flight Subject Migration Engine

- **Status:** Accepted
- **Date:** 2026-08-14
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Over 70% of Phase II/III clinical trials experience at least one mid-study protocol amendment (e.g., modifying dosage cohorts, adding a pharmacokinetic visit, or altering safety laboratory schedules). In legacy EDC platforms, applying a mid-study amendment requires system downtime, custom SQL data migration scripts, or manual dual entry across separate study instances.

Under 21 CFR Part 11, GAMP 5, and EU Annex 11, the platform must guarantee:

1. Zero system downtime during protocol amendment authoring, review, and publication.
2. Full immutability and non-destructive retention of clinical observation data recorded under previous protocol versions.
3. Automated gating of upcoming clinical visits for in-flight subjects until re-consent is completed when required (`PRD-SUB-007`).

## 2. Decision Drivers & Constraints

- **Zero-Downtime Graph Immutability:** Published study versions in Neo4j must never be modified in-place.
- **Non-Destructive Historical Retention:** Observations, visits, and form submissions completed under Version 1.0.0 must remain permanently bound to Version 1.0.0 schemas without schema rewriting.
- **Dynamic Subject Schema Projection:** In-flight subjects must dynamically execute visits and eCRF forms based on their active consented protocol version.
- **Re-Consent Gating Compliance (`PRD-SUB-007`):** When an amendment flags `requires_reconsent = True`, the system must prevent data entry on subsequent visits until a signed ICF matching the amended version is registered.

## 3. Options Considered

1. **Graph-Native Immutable Branching & Dynamic Projection (Selected):**
   - Protocol amendments clone the metadata subgraph into a new version node linked via `PREVIOUS_VERSION`.
   - Execution engine dynamically resolves schemas per subject active version.
   - Historical visits remain stamped and rendered with original version schemas.
   - Re-consent gating blocks upcoming visit data entry until signed consent is recorded.
2. **Global Database Schema Mutation & Retroactive SQL Migration:**
   - Overwriting existing database tables to match the new protocol version and running bulk migration scripts.
   - Rejected due to risk of historical data corruption, audit trail breakage, and mandatory system downtime.
3. **Multi-Instance Study Duplication:**
   - Spawning a completely new study instance for amended versions and requiring manual data transcription.
   - Rejected due to high operational friction and error risk.

## 4. Decision Outcome

Chosen option: **Graph-Native Immutable Branching & Dynamic Projection**.

- `apps/designer/domain/amendment_service.py` clones the active study version subgraph (Arms, Epochs, Encounters/Visits, Activities, Rules) into a new `DRAFT_AMENDMENT` node.
- `apps/execution/subject_lifecycle.py` enforces `validate_subject_version_gating` and raises `ReConsentRequiredException` when re-consent is missing.
- `apps/web/src/views/AmendmentDiffView.vue` and `EcrfView.vue` provide side-by-side visual diffs and in-situ re-consent gating and signing workflows.

## 5. Consequences & Trade-offs

- **Positive:** Zero platform downtime, complete 21 CFR Part 11 traceability, no data loss or corruption, transparent visual diffs, and automated re-consent compliance.
- **Trade-off:** Requires multi-version schema resolution in the execution engine and deep graph cloning in Neo4j.

## 6. Implementation & Verification

- **Backend Modules:**
  - `apps/designer/domain/amendment_service.py`
  - `apps/designer/adapters/repositories.py` & `apps/designer/delta.py`
  - `apps/execution/subject_lifecycle.py`
  - `apps/execution/database/models/subject.py`
  - `apps/execution/services/subject_migration.py`
  - `apps/execution/presentation/routers/amendments.py`
- **Frontend Modules:**
  - `apps/web/src/views/AmendmentDiffView.vue`
  - `apps/web/src/views/EcrfView.vue`
- **Verification Suite:**
  - `apps/execution/tests/test_amendment_migration.py`
  - `apps/designer/tests/test_protocol_amendments_validation_suite.py`
  - GxP compliance sync via `scripts/sync_gxp.py`
