# ADR-2157: RSA-PSS Cryptographic Padding for Electronic Signatures

- **Status:** Accepted
- **Date:** 2026-08-04
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

To meet the rigorous compliance standards of FDA 21 CFR Part 11 and ensure absolute authenticity and non-repudiation of clinical records, the Cadence Clinical Platform enforces asymmetric digital signing on critical regulatory operations. Previously, our signature verification engine used PKCS#1 v1.5 deterministic padding for RSA keys.

Deterministic padding schemes are susceptible to certain classes of cryptographic attacks (such as Bleichenbacher-style padding oracle attacks). To harden our electronic signature infrastructure against key extraction and signature forgery, we must transition all asymmetric RSA operations to use the Probabilistic Signature Scheme (PSS) padding, tracing directly to system requirement **PRD-SYS-001** (Cryptographic Integrity & GxP Compliance).

## 2. Decision Drivers & Constraints

- **Regulatory Compliance (PRD-SYS-001):** Mandates that all electronic signatures are highly secure, tamper-resistant, and implement modern industry-standard cryptographic primitives.
- **Cryptographic Strength:** Transition from deterministic signing to probabilistic signing to guarantee that two signatures produced on the same document are unique.
- **Backwards Compatibility:** We must ensure that any Elliptic Curve (ECDSA) and symmetric fallback signature strategies continue to operate seamlessly alongside the hardened RSA verification engine.

## 3. Options Considered

### Option A: RSA-PSS (Probabilistic Signature Scheme) with SHA-256 (Selected)

Standardize all RSA signature generation and verification on RSA-PSS padding with MGF1 (Mask Generation Function 1) and a SHA-256 hash function. This utilizes the maximum possible salt length for optimal security.

### Option B: PKCS#1 v1.5 Deterministic Padding

Maintain the legacy PKCS#1 v1.5 padding scheme. While simpler, it is cryptographically deprecated for new designs and fails to meet modern NIST/FIPS-140 guidelines for high-security electronic signatures.

## 4. Decision Outcome

Chosen option: **Option A** because it satisfies the rigorous security and GxP compliance requirements under **PRD-SYS-001** by incorporating a modern, non-deterministic signature padding scheme. It ensures that RSA digital signatures cannot be forged or manipulated via padding oracle vulnerabilities.

## 5. Consequences & Trade-offs

- **Positive:**
  - Implements modern, state-of-the-art cryptographic signature standard (NIST SP 800-56B compliant).
  - High entropy signature generation due to randomized padding.
  - Explicitly passes the strict GxP and Part 11 security validation gates.
- **Negative:**
  - Transitioning existing systems requires regenerating keys and invalidates legacy PKCS#1 v1.5 signatures (which has already been handled in the current V2 signature deprecation lifecycle).

## 6. Implementation & Verification

- **Implementation:** Modified `packages/security/signing.py` (`asymmetric_sign` and `asymmetric_verify` functions) to load RSA private/public keys and perform signing/verification using `padding.PSS` with `padding.MGF1(hashes.SHA256())` and maximum salt length.
- **Verification:** Run the standard cryptographic test suite under `tests/test_cryptography.py` and `tests/test_double_auth.py` to ensure that RSA signing/verification passes successfully.
