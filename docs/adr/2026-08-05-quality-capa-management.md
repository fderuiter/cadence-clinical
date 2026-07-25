# ADR-056: Quality & CAPA Management, Platform Placement, and Traceability

* **Status:** Accepted
* **Date:** 2026-08-05
* **Authors:** @jules
* **Deciders:** @fderuiter, @jules

---

## 1. Context & Problem Statement
Quality assurance, deviation reporting, Root Cause Analysis (RCA), and Corrective and Preventive Actions (CAPA) are crucial for GxP-regulated clinical trials. When protocol deviations occur (such as eligibility violations, IP temperature excursions, or informed consent errors), they must be logged, thoroughly investigated, and linked to systematic CAPA measures to ensure participant safety and study integrity.
Under FDA 21 CFR Part 11 and EU Annex 11, all deviations, RCAs, and CAPAs must remain strictly auditable, with changes recorded in a chronological append-only audit ledger containing explicit user identities, roles, timestamps, and change justification reasons. Additionally, the Quality & CAPA subsystem must be isolated to prevent database schema leakage and domain coupling with clinical trial execution (EDC) or administrative databases (CTMS).

## 2. Decision Drivers & Constraints
* **Compliance & Auditability:** Enforce mandatory change justifications, OIDC identities, and role contexts inside an immutable, chronological, append-only audit trail (`QualityAuditLog`).
* **Microservice Independence:** Prevent cross-domain relational leaks by separating Quality databases and services from trial execution databases (`apps/execution`) and study design graph models (`apps/designer`).
* **Optimistic Concurrency & Robustness:** Protect against concurrent overwrites on mutable QA records during collaborative investigator/sponsor team workflows.
* **Strict Authorization Rules:** Read-only roles (e.g., inspectors, auditors) must be restricted to viewing, general write roles (e.g., CRA, Investigator) can report deviations and document RCAs, and only designated Quality Oversight roles (e.g., Quality Manager, QA Lead) can approve or close CAPA records.

## 3. Options Considered
### Option 1: Monolithic Operational Workflows in `apps/execution`
* **Overview:** Embed deviation, RCA, and CAPA tables directly inside the core trial execution PostgreSQL database.
* **Pros:**
  * ✅ Simpler service topology with a single database to manage.
* **Cons:**
  * ❌ Severe relational coupling between temporary operational data (QA actions) and primary participant clinical values.
  * ❌ High risk of schema pollution and complex GxP validation cycles (a change to QA forms triggers validation for the entire EDC engine).
  * ❌ Difficult to scale, migrate, or selectively lock portions of the operational platform.

### Option 2: Standalone Quality Microservice with Isolated Relational Datastore (Selected)
* **Overview:** Deploy an independent microservice under `apps/quality` exposed through a dedicated gateway path proxy, utilizing its own database context (SQLite for local sandbox/testing, PostgreSQL for production), and routing through standard authentication middlewares (`GatewayAuthMiddleware`).
* **Pros:**
  * ✅ High modularity: Changes to Quality schemas have zero impact on clinical trial databases or CTMS.
  * ✅ Strong authorization controls: The gateway validates OIDC user tokens, allowing easy isolation of read-only, general write, and quality oversight boundaries.
  * ✅ Direct relational constraints: Uses strict database foreign keys to map Deviations to RCAs, and RCAs to CAPA Records.
* **Cons:**
  * ❌ Small operational overhead of maintaining independent server deployment boundaries and distinct database sessions.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Option 2 aligns with the Cadence Clinical microservice boundaries principles. It enables independent system validation, allows fine-grained role gating, and enforces absolute database decoupling, ensuring clinical values are never polluted by quality management flows.

## 5. Consequences & Trade-offs
* **Positive Impact:**
  * Clean domain separation: The quality schema contains clear traceability structures (`version_index`, `reason_for_change`, `created_by`, `created_at`).
  * Immutable logging: Complete ledger integrity via `QualityAuditLog` ensures GxP audit-readiness.
  * Robust state machine: Implements strict transition gates (e.g., CAPAs must go through UNDER_REVIEW and IMPLEMENTATION before EFFECTIVENESS_CHECK and CLOSED).
* **Negative Impact / Technical Debt:**
  * Requires explicit routing of `/api/v1/quality/*` paths through the gateway.
  * Dual-datastore setup configuration required (`QUALITY_DATABASE_URL`).
* **Mitigation Strategy:** Decouple gateway authentication via a reusable middleware `GatewayAuthMiddleware` that standardizes header validation and injects audit contexts into Request state.

## 6. Implementation & Verification
* **Affected Repositories / Services:**
  * `apps/quality/` (FastAPI backend service and models)
  * `apps/gateway/` (API routing, reverse proxy, and OpenAPI aggregation)
* **Verification Plan:**
  * **Unit/Integration Testing:** Verify full CRUD lifecycles, role-based forbidden checks, transition constraints, optimistic locking, and audit log atomicity in `tests/test_quality_workflow.py`.
  * **ADR Validation:** Ensure correct ADR indexing and file patterns via `python scripts/validate_adrs.py`.
