# ADR-120: Cryptographic Signature Verification Engine for Electronic Signatures

- **Status:** Accepted
- **Date:** 2026-08-01
- **Authors:** @jules
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

To meet the stringent standards of FDA 21 CFR Part 11 for electronic records and signatures, the platform requires a robust and verifiable method to verify digital signatures. Previously, signature verification logic was fragmented or lacked robust asymmetric key verification (RSA/ECDSA) along with symmetric HMAC fallback verification. We need a unified engine to handle electronic signature validation, tamper detection, and key/certificate loading. This implements the compliance tracing requirements in PRD-SYS-001.

## 2. Decision Drivers & Constraints

- **Driver 1 (Compliance):** Must comply with FDA 21 CFR Part 11 electronic signature regulations.
- **Driver 2 (Security):** Must support strong asymmetric cryptographic algorithms (RSA-SHA256, ECDSA-SHA256) and fallback to standard symmetric HMAC verification.
- **Driver 3 (Robustness):** Must cleanly handle signature and XML/JSON content parsing, stripping signatures during hash computation, and key/certificate serialization.

## 3. Options Considered

### Option 1: Basic string-based signatures with database audit tables only

- **Overview:** Rely purely on database constraints and simple token matching.
- **Pros:**
  - ✅ Extremely easy to implement.
- **Cons:**
  - ❌ Does not provide cryptographic proof of non-repudiation or tamper-evidence required by Part 11.

### Option 2: Full Asymmetric RSA/ECDSA verification with Symmetric HMAC fallback (Selected)

- **Overview:** Build a dedicated `crypto_verifier.py` service inside `packages/security/` that validates RSA and ECDSA signatures using base64 inputs, cleans/canonicalizes payload formats, parses signatures in PEM/XML/JSON shapes, and provides secure fallback to HMAC signatures.
- **Pros:**
  - ✅ High security and non-repudiation.
  - ✅ Flexible compatibility with modern identity providers and legacy clients.
  - ✅ Fully compliant with 21 CFR Part 11.
- **Cons:**
  - ❌ Additional complexity in managing public keys/certificates and handling payload canonicalization.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Implementing an advanced cryptographic verification engine directly fulfills PRD-SYS-001 and Part 11 GxP compliance requirements. It ensures tamper-evidence and binding authorization.

## 5. Consequences & Trade-offs

- **Positive Impact:** Secure, unified cryptographic verification of electronic signatures and batch manifests is now available to all gateway routers and execution services.
- **Negative Impact / Technical Debt:** Requires public keys or certificates to be present/loaded for asymmetric signature validation.
- **Mitigation Strategy:** Provide clear error codes (`INVALID_KEY`, `SIGNATURE_MISMATCH`, `MALFORMED_SIGNATURE`) to help diagnose failures in verification.

## 6. Implementation & Verification

- **Affected Repositories / Services:**
  - `packages/security/crypto_verifier.py`
- **Verification Plan:**
  - Verification is covered by comprehensive unit tests in `tests/test_clinical_validation_engines.py` and integration flows checking electronic signature validity.
