# ADR-2472: Enforce Fail-Closed E-Signature Validation and Standardize Clinical UI Primitives

* **Status:** Accepted
* **Date:** 2026-08-04
* **Authors:** @google-labs-jules[bot]
* **Deciders:** @fderuiter
* **Requirements:** PRD-TMF-002, PRD-SYS-003

---

## 1. Context & Problem Statement
Under FDA 21 CFR Part 11, electronic records and signatures must meet strict authenticity and integrity standards. Previously, the eTMF document ingestion pipeline permitted metadata parameters to bypass electronic signature checks. While useful for local integration testing, this created a critical regulatory compliance risk if accidentally triggered or exploited in production or staging environments.

Additionally, various shared clinical Vue components under `packages/ui` required code quality and formatting standardization (such as ruff format/check alignments) to resolve automated quality gate checks.

## 2. Decision Drivers & Constraints
* **Fail-Closed Security:** Unconditional enforcement of cryptographic signature validation in protected environments.
* **Developer Velocity:** Maintaining local development and testing efficiency by allowing overrides only in local/sandbox environments.
* **Code Quality & Uniformity:** Standardizing styling and formatting across backend and frontend packages.

## 3. Options Considered
### Option 1: Warning log instead of complete rejection
* **Overview:** Log warnings if overrides or mock signatures are used in protected environments but still allow the request.
* **Pros:**
  * ✅ Less disruptive to live environments if misconfigured.
* **Cons:**
  * ❌ Major regulatory non-compliance with FDA 21 CFR Part 11.
  * ❌ Fails to prevent unauthorized access or ingestion overrides.

### Option 2: Environment-Gated Fail-Closed Validation & Standardized Formatting (Selected)
* **Overview:** Block all bypasses, overrides, and mock signatures unconditionally in staging/production contexts. Run comprehensive formatters on shared clinical Vue templates to enforce a uniform style guide.
* **Pros:**
  * ✅ Full 21 CFR Part 11 compliance.
  * ✅ Zero risk of accidental production override.
  * ✅ Clean codebase adhering to quality gates.
* **Cons:**
  * ❌ Strict environment configuration is mandatory.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Implementing a fail-closed validation mechanism ensures that any attempt to bypass cryptographic signature validation in protected environments fails immediately. Standardizing Vue components completes the compliance pipeline.

## 5. Consequences & Trade-offs
* **Positive Impact:** 100% compliant eTMF document ingestion pipeline. Unified style compliance across clinical UI components.
* **Negative Impact / Technical Debt:** Tests in protected environments must simulate authentic payloads with valid cryptographic keys.
* **Mitigation Strategy:** Added comprehensive integration tests to mock sandbox and production environments separately.

## 6. Implementation & Verification
* **Affected Repositories / Services:**
  * `apps/etmf/`
  * `packages/ui/`
* **Verification Plan:**
  * Verified backend compliance using the pytest suite under `tests/test_fail_closed_sig_validation.py`.
  * Verified frontend formatting and linter passes using `pnpm -r format && pnpm -r lint`.
