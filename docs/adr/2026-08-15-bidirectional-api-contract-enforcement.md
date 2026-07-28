# ADR-095: Bidirectional API Contract Enforcement and Legacy Whitelisting

* Status: Accepted
* Date: 2026-08-15
* Authors: @jules
* Deciders: @fderuiter

---

## 1. Context & Problem Statement
Previously, the API validation suite only checked if documented endpoints existed in the codebase (unidirectional validation). This allowed active, undocumented routes and mismatched parameters to bypass API specification checks, risking silent contract drift. To bridge this gap, we need bidirectional enforcement to guarantee that all exposed APIs are accurately reflected in the Markdown API specification (`docs/SDLC/03_API_Integration_Specification.md`).

This decision implements requirements under PRD-MDR-001.

## 2. Decision Drivers & Constraints
* **Compliance & Drift Prevention:** Avoid API contract drift to maintain consistent system expectations for consumers.
* **Developer Velocity:** Enable active feature development on legacy endpoints that are not yet fully documented.
* **Performance Budget:** Ensure the validation checks complete within the existing target performance budget (under 200ms).

## 3. Options Considered
### Option 1: Full Strict Bidirectional Parity Enforced Instantly
* **Overview:** Require all endpoints (legacy and new) to be documented immediately.
* **Pros:**
  * ✅ Complete, absolute parity between the codebase and documentation.
* **Cons:**
  * ❌ Blocks existing developers working on legacy paths, causing a massive bottleneck in development velocity.

### Option 2: Bidirectional Parity with Normalized Whitelisting for Legacy Endpoints (Selected)
* **Overview:** Implement bidirectional route, method, and parameter validation, but register undocumented legacy endpoints in a static `WHITELISTED_ROUTES` registry to bypass enforcement during development transition phases.
* **Pros:**
  * ✅ Eliminates API drift on all new/updated endpoints.
  * ✅ Normalized route matching ensures version prefixes (e.g., `/api/v1`) are handled consistently.
  * ✅ Preserves developer velocity by not blocking legacy work.
* **Cons:**
  * ❌ Requires maintaining a temporary whitelist until legacy paths are fully documented.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Option 2 balances GxP-grade contract enforcement with practical development needs. By ensuring bidirectional validation for all active routes except normalized whitelisted legacy endpoints, we prevent new API drift while preserving team productivity.

## 5. Consequences & Trade-offs
* **Positive Impact:** API drift is completely halted for new developments. Request parameter mismatch issues are caught early during automated CI runs.
* **Negative Impact / Technical Debt:** A static `WHITELISTED_ROUTES` list exists in the test codebase and must be incrementally reduced as those endpoints are documented.
* **Mitigation Strategy:** Establish a periodic review to document and remove endpoints from the whitelist.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `tests/test_api_contract_validation.py`
* **Verification Plan:** Validated via unit/regression tests checking that undocumented new routes and parameter mismatches correctly raise errors, while whitelisted legacy routes bypass them successfully.
