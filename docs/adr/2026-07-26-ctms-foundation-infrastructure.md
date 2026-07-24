# ADR-046: CTMS Foundation, Database, Auditing, and RBAC Infrastructure

* **Status:** Accepted
* **Date:** 2026-07-26
* **Authors:** @fderuiter
* **Deciders:** @fderuiter, @jules

---

## 1. Context & Problem Statement
To establish monitoring, site operations, financial, and recruitment workflows, the Cadence Clinical platform requires an isolated, independently deployable Clinical Trial Management System (CTMS) bounded context. This context must be secured via our gateway, integrated with Keycloak OIDC authentication, auditable to comply with 21 CFR Part 11 requirements, and accessible via precise Role-Based Access Controls (RBAC).

## 2. Decision Drivers & Constraints
* **Compliance:** Strict alignment with FDA 21 CFR Part 11, GAMP 5, and EU Annex 11 standards requires mandatory audit fields (created_at, created_by, reason_for_change, version_index) and immutable append-only audit trails.
* **Separation of Concerns:** Keep the CTMS metadata, site, and recruitment operations decoupled from metadata graph modeling and EDC runtime execution.
* **Authentication & Authorization:** Downstream services must only allow authenticated and authorized roles to query or mutate CTMS data.

## 3. Options Considered
### Option 1: Monolithic Extension
Merge CTMS workflows into the execution engine (`apps/execution`) or designer service (`apps/designer`).
- ❌ Increases coupling between clinical design schemas and administrative/operational data.
- ❌ Harder to scale microservices independently.

### Option 2: Isolated CTMS Service with Gateway Integration (Selected)
Establish a separate FastAPI service under `apps/ctms/`, deployable as its own container stack service, with database isolation, central gateway auth/header propagation, and precise RBAC checks.
- ✅ Clear architectural boundary for CTMS.
- ✅ Supports environment-configured DB variables (`CTMS_DATABASE_URL`).
- ✅ Perfect separation of database schemas and operational microservice components.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Option 2 provides a decoupled architecture with clean service boundaries, enabling the team to build advanced monitoring and financial workflows without impacting EDC clinical schemas.

## 5. Consequences & Trade-offs
* **Positive Impact:** Standardized, isolated operational model, easily auditable via `CTMSAuditLog` entries. Supports high-velocity feature addition.
* **Negative Impact / Technical Debt:** Added microservice operational overhead (additional container, health checks, and gateway API mappings).
* **Mitigation Strategy:** Minimize deployment overhead through standardized FastAPI setups, unified Docker Compose stacks, and shared security middleware templates.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `apps/ctms`, `apps/gateway`, `docker/` (compose and Keycloak realm), and documentation.
* **Verification Plan:** Unit and integration testing using TestClient, verifying lifespan database initializations, RBAC authorization boundaries, and audit log generation.
