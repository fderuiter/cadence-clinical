# ADR-094: Deterministic GxP Report Generation and Signature Verification Rectification

* **Status:** Accepted
* **Date:** 2026-08-12
* **Authors:** @google-labs-jules[bot]
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement
To ensure continuous audit-readiness in compliance with 21 CFR Part 11 and GxP standards, the Cadence Clinical Platform requires automated generation of SDLC reports, including the Requirements Traceability Matrix (RTM) and Qualification Execution reports. 
However, non-deterministic file traversal and environment-dependent package listing (e.g., active pip environment scans) resulted in false-positive git diff failures in CI pipelines. 
Additionally, the signature verification fallback logic in `packages/security/signing.py` contained broken/incomplete syntax fragments causing overall linting and parser check failures.

## 2. Decision Drivers & Constraints
* **Driver 1:** Reliability of continuous integration verification gates.
* **Driver 2:** Compliance with GxP and 21 CFR Part 11 signature validation rules.
* **Driver 3:** Eliminating developer merge friction by avoiding non-deterministic git diffs.

## 3. Options Considered
### Option 1: Disable CI Git Diff Guardrails
* **Overview:** Remove strict git diff assertions in `.github/workflows/ci.yml`.
* **Pros:**
  * ✅ Eliminates pipeline failures.
* **Cons:**
  * ❌ Violates regulatory compliance by allowing uncommitted/untracked documentation updates to bypass audit trails.

### Option 2: Rectify Signature Logic & Standardize Deterministic Reporting
* **Overview:** Clean up incomplete signature fallbacks in `packages/security/signing.py` and enforce deterministic sorting in documentation generators.
* **Pros:**
  * ✅ Restores fully compliant signature validation fallback.
  * ✅ Eliminates non-deterministic report generation.
* **Cons:**
  * ❌ Requires careful regression validation of signature verify scenarios.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Choosing Option 2 guarantees deterministic build artifacts and continuous compliance while resolving all syntax/parsing issues, ensuring zero false-positive CI failures.

## 5. Consequences & Trade-offs
* **Positive Impact:** All pipelines now pass cleanly, documentation matches the exact code state, and signature validations are fully backward-compatible.
* **Negative Impact / Technical Debt:** Requires keeping report generation scripts synchronized with any new filesystem path conventions.
* **Mitigation Strategy:** CI automatically runs report validation on all PR checkouts.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `packages/security`, `scripts/`
* **Verification Plan:** Verify with `python3 scripts/validate_adrs.py`, `uv run ruff check .`, and `pnpm -r lint`.
