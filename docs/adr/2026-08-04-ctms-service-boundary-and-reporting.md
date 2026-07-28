# ADR-055: CTMS Service Boundary, Reporting, and Operational Workspace

* **Status:** Accepted
* **Date:** 2026-08-04
* **Authors:** @jules
* **Deciders:** @fderuiter, @jules

---

## 1. Context & Problem Statement
Administrative, financial, operational, and site monitoring activities (such as scheduling monitoring visits, recording findings, issuing follow-up correspondence, tracking milestones, and maintaining CRA workloads) are crucial parts of running a clinical trial. However, mixing these operational workflows with the core eCRF/EDC execution databases or the metadata graph designer violates our separation of concerns and complicates GxP system validation.
We need to establish a dedicated operational service boundary for the Clinical Trial Management System (CTMS), while exposing a robust, visible demonstration surface in the interactive web workspace.

This decision implements requirements under Trace-6.

## 2. Decision Drivers & Constraints
* **Compliance & Separation of Concerns:** Core clinical data capture (EDC) and administrative trial tracking (CTMS) have distinct lifecycles and compliance audit scopes.
* **Auditability (21 CFR Part 11):** Operations, milestone status updates, and visit reports must produce immutable audit records (`CTMSAuditLog`).
* **Visual Demonstration Surface:** The interactive web sandbox requires a visible CTMS section showing visit/milestone tracking and cryptographic ledger blocks for compliance verification.
* **No Unimplemented Synchronization Claims:** The architecture and documentation must remain realistic and not make claims about unimplemented cross-service recruitment synchronization.

## 3. Options Considered
### Option 1: Monolithic operational workflows in apps/execution
* **Overview:** Embed operational monitoring visits, recruitment trackers, and CRA allocations into the core execution database schema.
* **Pros:**
  * ✅ Avoids deploying an extra microservice.
* **Cons:**
  * ❌ Severe schema bloating of the core execution relational database.
  * ❌ Complicates GxP change management and system validation.

### Option 2: Decentralized CTMS Microservice & Standalone UI Workspace (Selected)
* **Overview:** Expose a modular FastAPI microservice (`apps/ctms`) with an isolated SQLite/PostgreSQL datastore, secured via Keycloak OIDC role-based routing at the gateway, and backed by comprehensive frontend demonstration tabs and packages/ui rendering.
* **Pros:**
  * ✅ High modularity and clear architectural boundaries.
  * ✅ Robust mock-based offline demonstration workspace.
  * ✅ Decoupled system validation and clear separation of database scopes.
* **Cons:**
  * ❌ Small overhead of maintaining dual datastore configurations and schema migrations.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Establishing an independent CTMS microservice is the only pattern that maintains clean service-oriented boundaries under FDA guidelines. It allows CTMS features to evolve, scale, and undergo GxP validation independently of the high-velocity clinical EDC schemas.

## 5. Consequences & Trade-offs
* **Positive Impact:**
  * Isolated database schema with standard Part 11 operational tracking.
  * Clean presentation layers via `packages/ui` for site milestones and visits.
* **Negative Impact / Technical Debt:**
  * No cross-service recruitment synchronization is currently implemented. Recruitment statistics are managed and updated explicitly within their respective service domains to avoid unvalidated distributed data risks.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `apps/ctms/`, `apps/web/`, `packages/ui/`
* **Verification Plan:**
  * Automated backend unit tests via `pytest tests/test_ctms.py`.
  * Automated frontend rendering tests in `apps/web/tests/ctms_ui.test.js`.
  * Automated indexing and validation check using `scripts/validate_adrs.py`.
