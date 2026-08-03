# ADR-252: Decommission Legacy V1 Signatures and Standalone Rendering Engine

* **Status:** Accepted
* **Date:** 2026-09-08
* **Authors:** @jules
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement
To maintain strict GxP and 21 CFR Part 11 electronic signature compliance, the Cadence Clinical platform must strictly enforce cryptographic signature validation using secure, canonical JSON structures (V2). The legacy V1 signature payloads (relying on simple colon-separated strings) represent an outdated validation pattern with lower cryptographic guarantees and must be completely decommissioned. Furthermore, the standalone, legacy clinical document rendering engine must be retired in favor of the unified, pre-compiled shared UI rendering library to prevent drift and ensure accessible, consistent UI layouts. Consolidating the permission matrix in `packages/security/rbac.py` requires removing duplicate `"soa"` resource keys to resolve Ruff linting/formatting rules (`F601`).

Requirements: PRD-SYS-001

## 2. Decision Drivers & Constraints
* **GxP & 21 CFR Part 11 Electronic Signature Compliance:** All signatures must be cryptographically verified using secure, canonicalized structures (V2).
* **System Uniformity & Prevent Rendering Engine Drift:** Retiring legacy standalone rendering blocks prevents UI inconsistencies between designer portals and actual user portals.
* **Strict Code Quality Gating:** All Ruff lint/formatting errors must be resolved, specifically duplicate dictionary keys in role permission lists.

## 3. Options Considered
### Option 1: Retain V1 Standalone Fallback as Deprecated
* **Overview:** Keep legacy rendering functions and simple string-based V1 signature validation paths while emitting deprecation logs.
* **Pros:**
  * ✅ Avoids immediate removal of backward-compatibility paths.
* **Cons:**
  * ❌ Leaves insecure cryptographic signatures accessible in codebase.
  * ❌ Increases maintenance overhead and potential for UI/UX drift between active rendering layouts.

### Option 2: Full Decommissioning and Security Matrix Consolidation [Selected]
* **Overview:** Completely remove V1 string signature paths, decommission the legacy standalone document rendering blocks, and consolidate all role permission mappings (including removing duplicate `"soa"` dictionary keys) in `packages/security/rbac.py`.
* **Pros:**
  * ✅ Absolute GxP/Part 11 security profile with exclusive V2 canonical validation.
  * ✅ Elimination of style/layout drift through unified rendering library.
  * ✅ Code clean, resolving lint F601 warnings immediately.
* **Cons:**
  * ❌ Requires updating legacy signature verifiers to throw 401 on V1 payloads instead of falling back.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Choosing Option 2 guarantees that the platform meets GxP standards for cryptographic integrity while eliminating redundant, unmaintained rendering code blocks, ensuring long-term code quality, security, and compliance.

## 5. Consequences & Trade-offs
* **Positive Impact:** Safer cryptographic signature gates, complete elimination of legacy rendering drift, clean Ruff linter metrics.
* **Negative Impact / Technical Debt:** Any extremely old cached/stored legacy clinical signatures must be upgraded or re-signed.
* **Mitigation Strategy:** Provide clear fallback migration paths or automated re-signing routines if legacy records are loaded.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `packages/security/rbac.py`, `apps/designer/soa_models.py`, `tests/test_rbac.py`
* **Verification Plan:** Verified through extensive test suites including api contract validations, linting rules, and rbac regression tests.
