# ADR-129: Shared eSignature Foundations

* **Status:** Accepted
* **Date:** 2026-08-28
* **Authors:** Jules
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement
Compliance with 21 CFR Part 11 requirements (PRD-SYS-001) dictates that electronic signatures have verified manifestations containing the printed name of the signer, the date/time of the signature, and the meaning (signing reason) associated with the signature.
We need a unified, core-level, service-agnostic model representing a signature manifestation, alongside cryptographic signing/verification primitives and context-propagation utilities to easily carry signer identity and reason through async call chains and audit logging.

## 2. Decision Drivers & Constraints
* **Part 11 §11.50 Compliance:** Establish printed name, timestamp, and meaning (SigningReasonCode) as system-declared, role-restricted values rather than free text.
* **Service Agnosticism:** Keep the core-models lightweight and importable across all services in the repository.
* **Cryptographic Soundness:** Implement asymmetric server certificate-bound signing and verification.
* **Audit Trail Traceability:** Propagate signature manifestations through contextual async variables to allow automated, non-repudiable audit trails.

## 3. Options Considered
### Option 1: Ad-hoc Signature Models per Service
* **Pros:** Simple to implement initially.
* **Cons:** Leads to fragmentation, inconsistent schemas across service boundaries, and lacks standardized validation or cryptographic primitives.

### Option 2: Unified, Lightweight Signature Manifestation and Cryptographic Primitives (Selected)
* **Pros:**
  * ✅ Single, canonical `SignatureManifestation` model in `packages/core-models/` that satisfies all §11.50 requirements.
  * ✅ Robust, centralized asymmetric signing and verification routines utilizing `cryptography`.
  * ✅ Context-propagating context variables `current_signature_manifestation` seamlessly integrated into the `audit_context` stack.
* **Cons:**
  * ❌ Requires mapping of existing legacy fields to ensure backwards compatibility.

## 4. Decision Outcome
We adopted **Option 2**. We populated the core models and packages as follows:
- **`packages/core-models/signature.py`:** Added `SigningReasonCode` and `ApprovalStatus` enums. Updated `SignatureManifestation` to carry the §11.50 fields (`signer_username`, `signer_full_name`, `signing_timestamp_utc`, `signing_reason_code`, `signing_reason_text`, `network_ip_address`, `device_user_agent`, `signature_hash_sha256`) while maintaining legacy backward-compatible fields via a bidirectional pre-validator.
- **`packages/security/signing.py`:** Extended with `serialize_manifestation_canonically`, `compute_manifestation_hash`, `sign_manifestation`, and `verify_manifestation` helpers, integrating server private key and certificate PEM loading (with transient development key generation fallbacks).
- **`packages/security/context.py`:** Extended with the `current_signature_manifestation` contextvar, exposed via `audit_context` and `audit_context_decorator`.

## 5. Consequences & Trade-offs
* **Positive Impact:** Standardized, Part 11 compliant signature manifestations across all services and automated, context-propagating audit trails.
* **Negative Impact / Technical Debt:** Requires a pre-validation bidirectional mapping in the model to sustain the legacy `SignatureManifestation` usage in older code paths, but this is a clean and isolated mitigation.
* **Mitigation Strategy:** The mapping logic is covered with comprehensive unit tests to prevent regressions.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `packages/core-models`, `packages/security`
* **Verification Plan:**
  * Verified using unit and integration tests in `tests/test_signature_manifestation.py`.
  * Verified both legacy and new Part 11 fields, asymmetric signing, signature verification, and async contextvar propagation.
