# ADR-2157: Transition to PSS Padding for RSA Signatures and Verifications

- **Status:** Accepted
- **Date:** 2026-08-04
- **Authors:** @jules
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

In the Cadence Clinical platform, digital signatures must comply with strict GxP (Good Clinical Practice) and 21 CFR Part 11 requirements for electronic records and signatures, as traced under **PRD-SYS-003** (Cryptographic Ledger Hashing & Chain Validation). Previously, RSA signatures were generated and verified using `PKCS1v15` padding. While widely supported, PKCS#1 v1.5 padding is mathematically older and less robust against certain side-channel and signature forgery attacks compared to modern padding schemes. To maintain the highest security standards and ensure alignment with the latest FDA cybersecurity draft guidelines for medical devices and clinical software, we need to upgrade our RSA signature schemes to a more secure probabilistic padding standard.

## 2. Decision Drivers & Constraints

- **GCP & FDA Compliance:** Digital signatures must be demonstrably secure against known cryptographic vulnerability classes, satisfying **PRD-SYS-003**.
- **Backward Compatibility:** Relational and graph audit trail verifications must remain fully stable and correct.
- **Deterministic Verification:** The signature generation and validation flows must be 100% reliable across parallel service executions.

## 3. Options Considered

### Option 1: Continue Using PKCS#1 v1.5 Padding

- **Overview:** Retain the legacy PKCS1v15 signature padding scheme.
- **Pros:**
  - ✅ Simplest approach; requires no changes to existing signing code.
- **Cons:**
  - ❌ Susceptible to a wider class of padding oracle attacks.
  - ❌ Does not align with the modern cryptographic standards recommended by NIST and FDA.

### Option 2: Transition to RSA-PSS (Probabilistic Signature Scheme) Padding

- **Overview:** Transition all RSA digital signatures and verifications to the modern PSS padding scheme, combined with SHA-256 and Mask Generation Function 1 (MGF1).
- **Pros:**
  - ✅ Offers provable security under the random oracle model.
  - ✅ Recommended by NIST, FDA, and major cryptographic bodies as the modern standard for RSA signatures.
  - ✅ High resistance to forgery and side-channel leakage.
- **Cons:**
  - ❌ Requires updating the cryptographic signing and verification utilities under `packages/security/`.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Transitioning to RSA-PSS with SHA-256 and maximum salt length provides mathematically robust security, ensuring our 21 CFR Part 11 compliance layer remains bulletproof and aligned with modern regulatory and industry best practices under **PRD-SYS-003**.

## 5. Consequences & Trade-offs

- **Positive Impact:** Drastically enhanced security posture for our cryptographic audit trails, electronic signatures, and Merkle tree block sealing across all clinical microservices.
- **Negative Impact / Technical Debt:** Requires a unified code and verification update across core packages to ensure all test signature fixtures conform to the new PSS standard.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/security/signing.py`
- **Verification Plan:** Validated via automated tests under `tests/test_part11_compliance_engine.py` and `tests/test_etmf_compliance.py`, adhering to the rules of **PRD-SYS-003**.
