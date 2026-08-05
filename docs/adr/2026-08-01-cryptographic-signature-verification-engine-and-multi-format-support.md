# ADR-189: Cryptographic Signature Verification Engine and Multi-Format Support

- **Status:** Accepted
- **Date:** 2026-08-01
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

To satisfy FDA 21 CFR Part 11 and clinical compliance requirements (PRD-SYS-001), the Cadence Clinical Platform must verify electronic signatures across multiple document types (including XML and JSON) and signature formats (such as PEM blocks, XML tags, JSON metadata, hex, and raw base64). Historically, the platform only supported basic symmetric HMAC-SHA256 signatures. Asymmetric cryptographic verification (RSA-SHA256, ECDSA-SHA256) and parsing/stripping capabilities are required to support secure, tamper-resistant document sign-offs, clinical validation, and multi-format integration.

## 2. Decision Drivers & Constraints

- **Compliance (PRD-SYS-001):** Electronic signatures must be programmatically verified, bound to the signer, and tamper-resistant under 21 CFR Part 11.
- **Algorithm Support:** Support both symmetric (HMAC-SHA256) and asymmetric (RSA-SHA256, ECDSA-SHA256) algorithms.
- **Format Flexibility:** Safely extract and verify signatures embedded within PEM blocks, XML tags (e.g., `<SignatureValue>`), JSON metadata (e.g., `signature_bytes_b64`), hex strings, and direct base64.
- **Tamper Prevention:** Support clean stripping of signatures and JSON key canonicalization to verify payload content hashes without including the signature itself in the hashed payload.

## 3. Options Considered

1. **Option A (Selected):** Build an integrated multi-format Cryptographic Signature Verification Engine in `packages/security/crypto_verifier.py` with support for RSA, ECDSA, and HMAC, XML/JSON parsing, and canonicalization.
2. **Option B:** Restrict the platform to simple symmetric HMAC signatures, requiring external services to perform asymmetric public-key certificate verification.

## 4. Decision Outcome

**Chosen option: Option A** because it natively integrates asymmetric and symmetric verification within the security package, satisfying PRD-SYS-001. It allows direct, offline, and high-performance verification of clinical documents, audit trails, and batch signatures across multiple formats (JSON, XML, PEM, Hex) while ensuring proper GxP-compliant signature isolation and payload canonicalization.

## 5. Consequences & Trade-offs

- **Positive:**
  - Complete, centralized support for 21 CFR Part 11 compliant e-signatures inside `packages/security/`.
  - Support for standard industry formats like PEM, XML, and JSON metadata.
  - Native ECDSA and RSA signature verification using the `cryptography` library.
- **Negative:**
  - Requires robust unit testing to handle various malformed or edge-case payloads.
  - Increased codebase complexity due to manual regex-based signature extraction and JSON/XML key parsing.

## 6. Implementation & Verification

- **Target files/packages modified:**
  - `packages/security/crypto_verifier.py`: Added support for RSA/ECDSA verification, signature extraction, stripping, and JSON key canonicalization.
- **Verification tests added under `tests/`:**
  - `tests/test_clinical_validation_engines.py`: Contains tests verifying the clinical validation engine and cryptographic signatures.
