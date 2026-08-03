# ADR-255: Decommission legacy V1 signatures and standalone rendering engine

* **Status:** Accepted
* **Date:** 2026-08-03
* **Authors:** @jules
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

The platform previously maintained both legacy V1 cryptographic signature validation paths and a standalone PDF/HTML rendering engine alongside the newer V2 canonical JSON-based signature verification and unified rendering pipelines. Retaining these legacy systems increases the security attack surface, compromises GxP 21 CFR Part 11 electronic signature compliance trace boundaries, and introduces maintenance overhead. This ADR formalizes the complete decommission of all V1 signature verification pathways and standalone rendering logic.

## 2. Decision Drivers & Constraints

* **Security & Compliance (PRD-SYS-001):** Enforce strict V2 canonical JSON signatures across all regulatory boundaries to guarantee non-repudiation and tamper evidence.
* **Maintainability:** Eliminate redundant rendering and signing code paths to streamline future development and audit logging.
* **Robustness:** Ensure 100% of signing actions are processed by the modern unified cryptography service.

## 3. Options Considered

### Option 1: Retain V1 Signatures as a Fallback
* **Overview:** Keep legacy pathways active but deprioritized.
* **Pros:**
  * ✅ Avoids potential breaking changes for old client versions.
* **Cons:**
  * ❌ Leaves legacy security vulnerabilities open.
  * ❌ Increases the compliance validation scope for GxP audits.

### Option 2: Full Decommission of V1 and Standalone Rendering (Selected)
* **Overview:** Delete V1 signing endpoints, remove standalone render configurations, and enforce unified V2 structures.
* **Pros:**
  * ✅ Zero legacy overhead.
  * ✅ Clean compliance boundaries.
  * ✅ Fully satisfies PRD-SYS-001.
* **Cons:**
  * ❌ Requires updating tests and whitelists.

## 4. Decision Outcome

* **Chosen Option:** Option 2
* **Justification:** Full decommissioning aligns with strict GxP requirements (PRD-SYS-001) by ensuring only the secure, modern V2 canonical signature verification engine and unified rendering engine are utilized in the platform.

## 5. Consequences & Trade-offs

* **Positive Impact:** Decreased attack surface, unified audit trailing, and cleaner codebase structure.
* **Negative Impact / Technical Debt:** Upstream clients must use V2 structures.
* **Mitigation Strategy:** Any legacy client attempts will be immediately rejected with clean error states and captured in the audit logs.

## 6. Implementation & Verification

* **Affected Repositories / Services:** `packages/security/rbac.py`, `apps/designer/`, `tests/`
* **Verification Plan:** Verify through the comprehensive backend test suite that V1 endpoints are removed and V2 signature paths are robustly enforced.
