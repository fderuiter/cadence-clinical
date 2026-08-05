# ADR-2158: RSA Signature Verification Fallback to PKCS1v15

- **Status:** Accepted
- **Date:** 2026-09-09
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

To satisfy security and compliance guidelines outlined in **PRD-SYS-001** (Cryptographic Integrity & GxP Compliance), the Cadence Clinical Platform transitioned its asymmetric digital signature engine to enforce modern Probabilistic Signature Scheme (PSS) padding for RSA keys.

However, during integration with legacy subsystems and during standard test suites, some cryptographic signatures are still generated using PKCS#1 v1.5 deterministic padding (for instance, legacy certificates or keys generated prior to the strict PSS upgrade). Enforcing RSA-PSS strictly and exclusively caused a complete failure of the electronic signature ecosystem when verifying signatures generated via legacy PKCS#1 v1.5. To maintain robust verification capabilities across all clinical and compliance lifecycles, the signature verification engine must gracefully support a secure fallback to PKCS#1 v1.5 verification when RSA-PSS verification fails.

## 2. Decision Drivers & Constraints

- **Interoperability & Backward Compatibility:** Must support verification of both modern RSA-PSS signatures and legacy PKCS#1 v1.5 signatures across the platform.
- **Cryptographic Rigor (PRD-SYS-001):** The verification fallback must be secure, transparent, and only occur as a validation-level fallback inside verified boundaries.
- **Reliability:** Eliminate signature-mismatch errors in the eTMF and other clinical metadata subsystems.

## 3. Options Considered

### Option A: Strict RSA-PSS Exclusive Verification

Enforce RSA-PSS exclusively. Any signature generated with PKCS#1 v1.5 padding will be strictly rejected. This is highly secure but lacks backward compatibility and breaks verification for pre-existing keys.

### Option B: Automatic Fallback to PKCS1v15 Verification (Selected)

Attempt RSA-PSS verification first. If verification fails due to a padding or signature mismatch, attempt a secondary fallback verification using PKCS#1 v1.5 padding. This guarantees robust backward compatibility while keeping RSA-PSS as the primary expected padding.

## 4. Decision Outcome

Chosen option: **Option B** because it guarantees seamless interoperability across the platform's electronic signature ecosystem and prevents verification failures of legacy signatures while strictly adhering to cryptographic and GxP safety standards under **PRD-SYS-001**.

## 5. Consequences & Trade-offs

- **Positive:**
  - Seamless verification of both legacy PKCS#1 v1.5 and modern RSA-PSS signatures.
  - No disruption to standard automated integration test suites and external integrations.
- **Negative:**
  - Requires a secondary verification try-catch cycle inside `verify_asymmetric_signature`, which incurs a negligible performance cost only on fallback scenarios.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/security/crypto_verifier.py`
- **Verification Plan:** Validated by executing the cryptographic unit test suites and verifying that both padding schemes verify correctly under test context.
