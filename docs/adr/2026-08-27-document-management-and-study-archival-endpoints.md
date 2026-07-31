# ADR-118: Exposing Document Storage and Study Archival REST Endpoints

* **Status:** Accepted
* **Date:** 2026-08-27
* **Authors:** @jules
* **Deciders:** @fderuiter, @reviewer

---

## 1. Context & Problem Statement
To support the frontend Vue 3 SPA document manager view and let authorized auditors and study managers access regulated trial records, we need REST API endpoints for document upload, metadata indexing, version history browsing, watermarked PDF/file download, and background study archival package generation. These must adhere to 21 CFR Part 11 auditing and GxP standards.

This decision implements requirements under PRD-SYS-001.

## 2. Decision Drivers & Constraints
* **Driver 1 (Compliance):** Enforce strict role-based access control (RBAC) permissions (`documents:read`, `documents:write`, and `archive:export`) registered as first-class permissions.
* **Driver 2 (Auditability):** Record GxP-compliant audit logs with action `DOCUMENT_VIEW` during content downloads.
* **Driver 3 (Usability & Safety):** Dynamically watermark downloads for auditors to prevent unauthorized propagation, and perform ZIP packaging asynchronously via background tasks.

## 3. Options Considered
### Option 1: In-database BLOB storage
* **Overview:** Save document binary contents in the primary database.
* **Pros:**
  * ✅ Simplifies transactions.
* **Cons:**
  * ❌ Increases database footprint significantly.

### Option 2: Microservice-isolated in-memory storage & dynamic watermarking
* **Overview:** Expose a dedicated Documents router under the execution microservice using format-agnostic watermarking and background tasks for archival, referencing core Pydantic v2 schemas.
* **Pros:**
  * ✅ Lightweight, format-agnostic, and self-contained.
  * ✅ Seamless integration with `AuditLog` table and `GatewayAuthMiddleware`.
* **Cons:**
  * ❌ Relies on memory buffers for large files (mitigated via streaming responses).

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Choosing Option 2 isolates the document management routing cleanly while leveraging the existing GxP audit trail and gateway security middleware.

## 5. Consequences & Trade-offs
* **Positive Impact:** Secure, format-agnostic copy watermarking and robust 21 CFR Part 11 audit logging of view actions.
* **Negative Impact / Technical Debt:** Memory-based caching is transient across app restarts (to be persisted to S3/LocalStorage provider in production).
* **Mitigation Strategy:** Local unit and integration tests under `tests/test_document_router.py` verify all states and persistence boundaries.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `apps/execution/routers/documents.py`, `apps/etmf/routers/archive.py`, `packages/core-models/storage/document_models.py`, `packages/security/permissions.py`
* **Verification Plan:** Verified via `tests/test_document_router.py` covering uploads, watermarked downloads, audit trail generation, and background archival jobs.
