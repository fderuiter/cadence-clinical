# ADR-118: 21 CFR Part 11 Batch Electronic Signatures for PI Casebook Sign-Off

* **Status:** Accepted
* **Date:** 2026-08-27
* **Authors:** @jules
* **Deciders:** @lead-architect, @qa-validator

---

## 1. Context & Problem Statement
Under FDA 21 CFR Part 11 and EU Annex 11 regulations, when a Principal Investigator (PI) performs an electronic signature to approve a complete subject casebook or multiple visit eCRFs at once, the system must enforce strict identity re-authentication and provide unambiguous execution provenance. The application requires a secure, dual-component (password and optional TOTP) batch signature modal and state store in the single page application (SPA) to safely capture credentials, compute content digests, and manifest the finalized signature certificates.

This ADR specifically relates to requirements under **PRD-SYS-001**.

## 2. Decision Drivers & Constraints
* **Driver 1:** Compliance with FDA 21 CFR Part 11 re-authentication and auditing.
* **Driver 2:** High-fidelity user experience for reviewing batch components and cryptographic previews.
* **Driver 3:** Security isolation and prevention of credential leakage in local state.
* **Constraint:** Must align with the existing `apiClient` routing, Pinia store boundaries, and OpenAPI contracts.

## 3. Options Considered
### Option 1: Incremental Individual Form Signatures
* **Overview:** Prompt the user to re-authenticate separately for each individual eCRF form.
* **Pros:**
  * ✅ Simplifies implementation by reusing single-signature modals.
* **Cons:**
  * ❌ Unacceptable user fatigue when signing off on tens of forms in a subject casebook.
  * ❌ Does not allow atomic transaction boundaries for multi-form sign-offs.

### Option 2: 21 CFR Part 11 Compliant Batch eSignature Modal & Store [Selected]
* **Overview:** Provide a specialized multi-step modal displaying the forms list with SHA-256 preview hashes, prompting for dual-component credentials and controlled meanings, and manifesting a confirmation certificate upon success.
* **Pros:**
  * ✅ High usability with a summary list of all batch forms.
  * ✅ Absolute regulatory compliance with dual-component password re-authentication and controlled meanings.
  * ✅ Atomic transaction boundary via a single secure API request.
* **Cons:**
  * ❌ Requires creating a dedicated Vue modal component and Pinia state store.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Option 2 satisfies both the regulatory requirements of 21 CFR Part 11 and the operational efficiency needs of PIs by allowing atomic, secure batch signatures with complete cryptographic validation and re-authentication.

## 5. Consequences & Trade-offs
* **Positive Impact:**
  * ✅ Complete compliance with Part 11 re-authentication mandates.
  * ✅ Clear visual feedback of cryptographic preview hashes and confirmation serials.
  * ✅ Zero password leaks in local state.
* **Negative Impact / Technical Debt:**
  * ❌ Incremental frontend maintenance overhead for the batch signature modal and store.

## 6. Implementation & Verification
* **Affected Repositories / Services:**
  * `apps/web/src/components/signatures/BatchSignatureModal.vue`
  * `apps/web/src/stores/signatures.ts`
  * `apps/web/tests/components/BatchSignatureModal.spec.ts`
* **Verification Plan:**
  * Validate using Vitest component and store tests.
  * Ensure requirements mapping to `PRD-SYS-001` via test docstrings.
