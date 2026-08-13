# ADR-[NUMBER]: GxP Change Justification Path Exemptions

- **Status:** Accepted
- **Date:** 2026-08-13
- **Authors:** @google-labs-jules[bot]
- **Deciders:** @fderuiter
- **Requirements:** PRD-CRF-010, Trace-28

---

## 1. Context & Problem Statement

The Cadence Clinical platform strictly enforces 21 CFR Part 11 and GxP compliance, which requires all mutating API requests to include a cryptographic gateway signature and a non-empty `X-Change-Reason` header (justification for change). This audit trial is vital for regulated environments (such as clinical execution/EDC and production settings).

However, during early stage development, design phase modifications, mock trials, and UI interactions on low-risk paths (such as draft study versions, staging configurations, visual canvas layouts, and visual study designer operations), requiring change justifications from the user at every single action creates high friction and slows down study builders. 

We need a safe, robust, and compliant way to allow path-based exemptions to bypass change justification verification for non-regulated, low-risk activities, without weakening enforcement on regulated endpoints.

## 2. Decision Drivers & Constraints

- **Developer Velocity / User Experience:** Study builders should not be repeatedly prompted for reasons for change when working on visual canvas, layout designing, draft, or staging environments.
- **GxP & Part 11 Regulatory Compliance:** Critical regulated endpoints (e.g. clinical execution/EDC) must always strictly enforce and validate reasons for change.
- **Security Middleware Consistency:** The API gateway and downstream middleware must continue to verify gateway signatures securely and reject malformed/unauthorized requests.

## 3. Options Considered

### Option 1: Globally relax reasons for change validation
- **Overview:** Relax reason-for-change requirements globally across all environments and stages.
- **Pros:**
  - ✅ Simplifies implementation.
- **Cons:**
  - ❌ Directly violates GxP and FDA 21 CFR Part 11 requirements for change justification and audit trails.

### Option 2: Implement centralized, path-based exemptions (Selected)
- **Overview:** Define a helper function `is_path_exempt_from_justification` in `packages/security/gating.py` listing low-risk keyword/path matches (e.g., `draft`, `staging`, `designer`, `layout`, `canvas`). If a request targets an exempt path, the middleware allows bypassing the change justification check (setting it to an empty string internally) while still strictly enforcing the check on all other paths.
- **Pros:**
  - ✅ Highly granular and safe: isolates non-regulated designer activities from execution-service endpoints.
  - ✅ Seamlessly syncs with frontend-side bypass checks.
  - ✅ Keeps standard signature verification fully functional.
- **Cons:**
  - ❌ Requires careful path matching to avoid false positives (e.g., excluding version IDs in execution tests, handled by targeting specific paths/activities).

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Centralized path-based exemptions satisfy both goals: maintaining maximum GxP regulatory compliance on critical execution workflows while greatly improving developer/designer user experience for low-risk visual/draft actions.

## 5. Consequences & Trade-offs

- **Positive Impact:** Bypasses visual design and draft modification justification prompts, resulting in a cleaner and faster study designer workspace.
- **Negative Impact / Technical Debt:** Slight overhead of maintaining matching rules in `packages/security/gating.py`.
- **Mitigation Strategy:** Any new regulated or non-regulated path naming patterns should be documented and updated in the gating helper.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/security/`, `apps/gateway/`, `apps/web/`
- **Verification Plan:**
  - Added backend unit tests under `apps/gateway/tests/test_security_middleware.py`.
  - Added frontend unit tests under `apps/web/tests/designer_bypass.test.js`.
  - Verified with full workspace-wide `pytest` run.
