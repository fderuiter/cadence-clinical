# ADR-119: NCI EVS REST API Integration and Automated Sync Client

* **Status:** Accepted
* **Date:** 2026-07-31
* **Authors:** Jules
* **Deciders:** @fderuiter
* **Requirement References:** PRD-SYS-001

---

## 1. Context & Problem Statement

The Cadence Clinical platform requires a robust integration with the NCI Enterprise Vocabulary Services (EVS) REST API to support controlled terminology (CT) search, validation, and automated synchronization of study design concept codes. This integration must be fully GxP 21 CFR Part 11 compliant.

To achieve this, we need:
1. An automated sync client that retrieves the latest definitions from EVS for any concept code referenced in a clinical study design.
2. A durable tracking mechanism for synchronization events in both the Metadata Designer (Neo4j/in-memory) and EDC Execution Engine (PostgreSQL/SQLModel).
3. A secure, role-gated API gateway boundary preventing unauthorized trigger actions while supporting standard GxP audit provenance (such as capturing the user identity and change justification reasons).

## 2. Decision Drivers & Constraints

* **Regulatory Compliance (FDA 21 CFR Part 11 & GxP):** Every modification and terminology synchronization trigger must capture the user id, timestamp, change justification reason (`X-Change-Reason`), and increment the version index of the synchronized record/job.
* **Resilience:** The sync client must gracefully handle EVS REST API network disconnects, rate limits, and service degradations by using stale caches and mock fallbacks without returning 5xx server errors.
* **Separation of Concerns:** Keep the design-time (Designer) and runtime (Execution/EDC) sync operations distinct but aligned on the API signature.

## 3. Options Considered

1. **Option 1: Direct, non-gated in-process sync calling NCI EVS without persistence.**
   * This would update the cache on the fly but provide no GxP audit trail or visibility into *when* the synchronization happened, *who* initiated it, or *why*.
2. **Option 2: Durable, audited sync endpoint and database state representation with gateway permission/scope verification (Selected).**
   * This option defines `EVSTerminologySyncJob` with GxP columns, tracks sync statuses, registers secure `/api/v1/studies/{study_id}/terminology/sync` endpoints, and implements a responsive Vue 3 interactive modal in the frontend workspace.

## 4. Decision Outcome

Chosen option: **Option 2**. It fulfills all regulatory and functional mandates.

We will:
* Define Pydantic models for `TerminologySyncRequest` and `TerminologySyncResponse`.
* Define a SQLAlchemy table `EVSTerminologySyncJob` under `apps/execution/database/models.py`.
* Implement the sync routes in `apps/designer/main.py` and `apps/execution/main.py` with OIDC Gateway authorization (`require_permission` and `require_study_scope`).
* Implement interactive Vue 3 UI integration within `MdrView.vue` utilizing Pinia and standard Keycloak session states.

## 5. Consequences & Trade-offs

* **Positive Consequences:**
  * Complete, 100% compliant GxP audit ledger of terminology synchronization events.
  * Improved clinician user experience with a dedicated, responsive UI trigger.
  * Resilient, fail-soft cache fallbacks during NCI EVS REST API outages.
* **Negative Consequences:**
  * Slightly increased API payload complexity due to mandatory `change_reason` and GxP fields.

## 6. Implementation & Verification

* **Modified Components:**
  * `apps/designer/main.py`: Add `sync_study_terminology_endpoint`.
  * `apps/execution/main.py`: Add `sync_study_terminology_execution_endpoint`.
  * `apps/execution/database/models.py`: Implement `EVSTerminologySyncJob`.
  * `apps/web/src/api/terminologyClient.js`: Integrate `syncStudyTerminology`.
  * `apps/web/src/views/MdrView.vue`: Build UI trigger and modal handlers.
* **Verification Plan:**
  * Run ruff validation: `uv run ruff check .`
  * Execute pytest suite: `uv run pytest tests/test_main.py tests/test_rbac_permissions.py --no-cov`
