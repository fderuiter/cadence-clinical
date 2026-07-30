# ADR-107: CTMS Offline Sync Reconciliation and Structural Clinical Queries

* **Status:** Accepted
* **Date:** 2026-08-25
* **Authors:** @jules
* **Deciders:** @architect, @sponsor_dm
* **Requirement References:** PRD-SYS-001, Trace-6, PRD-CTMS-002

---

## 1. Context & Problem Statement
The clinical trial monitoring workspace requires CRAs to perform on-site monitoring visits (MVRs). These environments are often network-restricted, necessitating support for offline-first data capture and asynchronous synchronization. Historically, conflict resolution has been managed within the interoperability layer. However, introducing a separate `MonitoringVisit` model to `apps/interop` would violate core service boundaries.

The CTMS (Clinical Trial Management System) service requires a robust, GxP and 21 CFR Part 11-compliant offline sync mechanism to reconcile site monitoring visits and findings while preserving complete audit trails, handling server-side deduplication, and raising structural queries for missing/deleted targets.

## 2. Decision Drivers & Constraints
* **Driver 1 (Service Isolation):** Maintaining a single authoritative source of truth for the `MonitoringVisit` domain inside `apps/ctms`, avoiding model duplication in `apps/interop`.
* **Driver 2 (Compliance):** Satisfying FDA 21 CFR Part 11 and GxP requirements by ensuring immutable audit logging, version index tracking, and defeated data retention.
* **Driver 3 (Code Reuse):** Reusing the proven, deterministic `apps/interop/sync_engine.py` reconciliation algorithm via an in-process integration contract.
* **Driver 4 (No Cross-Service Sync):** Explicitly acknowledging and extending the "no cross-service sync" statement in ADR-055 to ensure services do not bidirectionally coordinate mutations across boundaries.

## 3. Options Considered
### Option 1: Duplicate `MonitoringVisit` in `apps/interop`
* **Overview:** Duplicate the monitoring models and endpoints inside `apps/interop` to utilize the existing sync gateway directly.
* **Pros:**
  * ✅ Requires no new sync controllers in CTMS.
* **Cons:**
  * ❌ Violates service boundaries and domain ownership.
  * ❌ Creates a split source of truth for MVRs, increasing maintenance overhead.

### Option 2: Build a custom, independent Sync Engine in CTMS
* **Overview:** Write a completely new, bespoke synchronization and merge logic from scratch inside CTMS.
* **Pros:**
  * ✅ Highly decoupled.
* **Cons:**
  * ❌ Duplicates complex LWW and merge logic.
  * ❌ Increases the surface area of potential bugs and divergence in conflict resolution behavior.

### Option 3: Narrow In-Process Sync Contract with Interop Sync Engine (Selected)
* **Overview:** Re-use the domain-agnostic `apps/interop/sync_engine.py` functions (such as `reconcile_records`) in-process within CTMS. CTMS owns the offline sync payloads, endpoints, defeated storage, and clinical-query database models.
* **Pros:**
  * ✅ Maintains clean domain isolation—the interop service is not called, and no models are duplicated.
  * ✅ Ensures identical conflict resolution semantics (LWW, Client Wins, Server Wins, Merge) by sharing the tested `sync_engine.py` library.
  * ✅ Simplifies offline integration with safe server-derived deduplication keys.

## 4. Decision Outcome
* **Chosen Option:** Option 3
* **Justification:** Reusing `sync_engine.py` as a narrow, domain-agnostic in-process utility provides the perfect balance of code reuse and architectural cleanliness. It keeps the authority over monitoring visit states entirely within CTMS, while preventing any "cross-service sync" coordination across services (abiding by ADR-055).

## 5. Consequences & Trade-offs
* **Positive Impact:**
  * Authoritative ownership of monitoring visits is strictly held by CTMS.
  * Structural conflicts (e.g. syncs referencing deleted/missing visits) are handled safely and promoted to actionable, open clinical-query records.
  * Full GxP compliance with immutable `CTMSAuditLog` entries for every reconciliation decision.
* **Negative Impact / Technical Debt:**
  * Requires importing the `apps/interop/sync_engine.py` module in-process across microservices, creating an in-repo dependency from CTMS to Interop's utility.
* **Mitigation Strategy:** Keep `apps/interop/sync_engine.py` strictly domain-agnostic and free of database imports so that it can be safely imported as a utility anywhere in the codebase.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `apps/ctms/models.py`, `apps/ctms/main.py`, `packages/security/rbac.py`
* **Verification Plan:**
  * Automated testing in `tests/test_ctms.py` validating happy path offline syncs, CLIENT_WINS/SERVER_WINS/MERGE conflict strategies, duplicate replay idempotency, structural conflict handling with query creation, and RBAC permission checks.
