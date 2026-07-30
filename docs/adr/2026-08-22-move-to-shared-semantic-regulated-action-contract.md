# ADR-099: Shared Semantic Regulated-Action Contract for Step-Up Re-Authentication

* **Status:** Accepted
* **Date:** 2026-08-22
* **Authors:** Jules
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement
Prior to this ADR, step-up re-authentication under 21 CFR Part 11 and EU Annex 11 was gated by URL-substring matching (e.g. searching for `"approve"` or `"sign-off"`). While functional, this substring-only approach:
1. Failed to capture body-driven regulated transitions (such as status updates to `CLOSED` or `CANCELLED` in Quality CAPAs, or status updates to `APPROVED` in CTMS Investigator Grants).
2. Suffered from architectural drift between API gateway and downstream service middlewares.
3. Left body-driven mutations outside the scope of regulatory compliance signature gates.

This ADR resolves these issues by implementing a unified, shared semantic regulated-action contract across all services, implementing requirements under PRD-SYS-001.

## 2. Decision Drivers & Constraints
* **GxP 21 CFR Part 11 Compliance:** Require double-keying re-authentication for all critical approval and closure actions, including those defined within HTTP bodies.
* **Architecture Harmonization:** Share a single semantic catalog of actions and their detection rules between the API Gateway and downstream middleware to eliminate logic drift.
* **Backward Compatibility:** Seamlessly handle legacy tokens which only support loose path-substring matching.

## 3. Options Considered
### Option 1: Inline body parsing at both gateway and downstream middlewares
* **Pros:** Highly decoupled.
* **Cons:** Leads to massive code duplication and drift between gateway and downstream services.

### Option 2: Unified Shared Semantic Regulated-Action Contract (Chosen)
* **Pros:**
  * Defines stableNamespaces / String constants (e.g., `quality.capa.close`, `quality.capa.cancel`, `ctms.grant.approve`).
  * Concentrates all detection logic (HTTP method, path pattern/regex, and body conditions) in a single module `packages/security/regulated_actions.py`.
  * Centralizes signature token verification in a reusable `verify_sig_token` helper.
  * Allows body-driven transitions to be authenticated seamlessly while keeping ordinary updates (such as budget/currency updates on draft grants) completely ungated.

## 4. Decision Outcome
We adopted Option 2. A central module `packages/security/regulated_actions.py` defines the stable namespaced strings, rules, and resolvers (`resolve_regulated_action` and `resolve_regulated_action_by_path`). The `X-Sig-Token` claims schema has been extended with `semantic_action` and `sig_ver` to bind the token securely to both the semantic action and concrete resource path.

## 5. Consequences & Trade-offs
* **Positive Impact:**
  * Strict Part 11 compliance for body-driven mutations in Quality and CTMS modules.
  * Eliminates drift between gateway and downstream middlewares.
  * Preserves frictionless non-regulated updates.
* **Negative Impact / Technical Debt:**
  * Increased validation overhead, requiring request body buffering and parsing (handled highly efficiently via Starlette request body caching).

## 6. Implementation & Verification
* **Affected Repositories / Services:** API Gateway, Quality & CAPA, CTMS, Security package.
* **Verification Plan:** Verify implementation using unit/integration tests in tests/test_gateway.py, tests/test_security_middleware.py, tests/test_quality_workflow.py, and tests/test_ctms.py.
