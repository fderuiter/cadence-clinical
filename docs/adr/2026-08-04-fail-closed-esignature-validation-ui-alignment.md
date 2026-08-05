# ADR-2472: Fail-Closed E-Signature Validation and UI Component Alignment

* **Status:** Accepted
* **Date:** 2026-08-04
* **Authors:** @google-labs-jules[bot]
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement
Under FDA 21 CFR Part 11 and EU Annex 11, electronic records and signatures must meet strict authenticity and integrity standards. Previously, the eTMF document ingestion pipeline permitted metadata parameters to bypass electronic signature checks during testing, creating a critical regulatory compliance risk if accidentally triggered or exploited in production or staging environments.

To eliminate this vulnerability, a robust, environment-gated, **fail-closed validation system** is required to strictly block all unauthorized signature overrides, legacy bypasses, and mock signatures in protected environments (production and staging) while preserving frictionless testing workflows in local and sandbox environments. Additionally, shared Vue 3 components and the clinical field views within `packages/ui` must be aligned with these strict validation parameters to ensure unified client-side presentation and avoid layout mismatches.

This ADR maps directly to requirements:
- **PRD-SYS-001**
- **Trace-13**
- **Trace-17**

## 2. Decision Drivers & Constraints
* **Compliance:** Enforce absolute compliance with FDA 21 CFR Part 11 signature/audit requirements.
* **Security & Fail-Closed Behavior:** Any environment configuration or signature check failure must result in rejection (fail-closed) in staging and production.
* **Developer Velocity:** Retain flexible mock and bypass testing capabilities for local, sandbox, and automated testing environments.
* **UI Interface Alignment:** Maintain unified interfaces and strict styling structures within `packages/ui` and `apps/web` without breaking existing workflows.

## 3. Options Considered
### Option 1: Fragmented Ad-hoc Router Verification
* **Overview:** Check environments and signature validity inline within each separate router function or API endpoint.
* **Pros:**
  * ✅ Quick to implement for isolated endpoints.
* **Cons:**
  * ❌ Extreme risk of code drift, duplication, and accidental omissions across different microservice paths.

### Option 2: Centralized Environment-Gated Cryptographic Validation and UI Field Integration
* **Overview:** Centralize environment context parsing and mock verification within `apps/etmf/cryptography.py`. Integrate and standardize component-level validation variables in `packages/ui` and `apps/web` to cleanly handle signature/clinical state transitions.
* **Pros:**
  * ✅ High security: uniform enforcement across the entire eTMF ingest boundary.
  * ✅ Fail-closed behavior is strictly guaranteed by checking system-level env variables.
  * ✅ Front-end and back-end operate on a consistent validation schema.
* **Cons:**
  * ❌ Requires updates across both backend services and frontend packages, including `packages/ui` clinical layouts.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Choosing Option 2 guarantees uniform compliance with Part 11 requirements across all environments. By centralizing the environment-gating inside the cryptographic validation module, we avoid logic fragmentation and prevent accidental bypasses in production. Standardizing Vue components in `packages/ui/src/components/clinical` ensures consistent visual feedback for signature states.

## 5. Consequences & Trade-offs
* **Positive Impact:** Strict fail-closed security for all clinical ingestion processes in staging and production, fully automated and robust unit tests, and perfect alignment between UI layouts and core validation states.
* **Negative Impact / Technical Debt:** Requires keeping frontend style dependencies and layouts in sync with backend model expectations.
* **Mitigation Strategy:** Automated GxP verification sync script (`sync_gxp.py`) and pre-merge pipelines are continuously run to detect any integration or validation drifts.

## 6. Implementation & Verification
* **Affected Repositories / Services:**
  * `apps/etmf` (Core cryptographic pipeline)
  * `packages/ui` (Shared Vue clinical input and layout components)
  * `apps/web` (Ctms and clinical presentation views)
* **Verification Plan:**
  * Verified by `tests/test_fail_closed_sig_validation.py` across different simulated environment contexts.
  * Validated via `scripts/validate_adrs.py` and the GxP compliance test suite.
