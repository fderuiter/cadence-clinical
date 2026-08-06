# ADR-2160: Part 11 Electronic Signature Compliance Engine Improvements

* **Status:** Accepted
* **Date:** 2026-08-06
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

To meet 21 CFR Part 11 Electronic Records and Electronic Signatures regulations, electronic signatures must be authentic, high-integrity, and secure against tampering. Cryptographic verification of signatures on clinical records (such as those in the electronic Trial Master File, or eTMF) must employ strong, non-deterministic signature padding schemes. Weak deterministic padding schemes, duplicate/injected certificate block exploits, and unauthorized validation bypasses present significant compliance and security risks. 

This decision addresses the following vulnerabilities and gaps traced to **Trace-13** (Native Part 11 eSignature Workflow & Post-Signature Mutation Rejection):
1. **Legacy Padding Vulnerabilities**: PKCS#1 v1.5 deterministic padding is susceptible to padding oracle attacks and lacks the modern cryptographic strength of RSA-PSS padding with SHA-256.
2. **Block Injection Attacks**: Exploit vectors where duplicate certificate blocks (`-----BEGIN CERTIFICATE-----` or `-----BEGIN SIGNATURE-----`) or duplicate XML signature blocks are injected into signed data to mislead verification mechanisms.
3. **Validation Bypass Vectors**: Ensure that signature verification bypass requests for mandatory regulatory documents are strictly rejected when GxP/strict compliance is active or mock configurations are disallowed.

## 2. Decision Drivers & Constraints

* **GxP / 21 CFR Part 11 Compliance (Trace-13)**: Ensuring high-integrity, tamper-evident document validation with strict cryptographic verification guidelines.
* **Deterministic vs. Probabilistic Padding**: Enforcing modern RSA-PSS signature padding as the baseline for all validated records, and actively rejecting legacy PKCS#1 v1.5 padding with explicit compliance alerts.
* **Tamper Prevention**: Strict boundaries on input payload structures to prevent signature wrapping or block injection.
* **Backward Compatibility / Informative Audits**: Generating clear audit log entries, compliance alert messages, and specific failure statuses when a signature is rejected due to insecure legacy schemes rather than a general verification error.

## 3. Options Considered

### Option A: Strict Enforced RSA-PSS Verification with Explict Legacy Rejection (Selected)
Enforce RSA-PSS (Probabilistic Signature Scheme) with SHA-256 padding for RSA public key signatures. If a signature fails RSA-PSS verification, check if it was signed using legacy PKCS#1 v1.5 padding. If PKCS#1 v1.5 verification succeeds, explicitly log a `COMPLIANCE ALERT` to the audit trails and return a designated `LEGACY_PADDING_REJECTED` failure to prevent the insecure signature from being accepted. Additionally, validate that exactly one certificate/signature block exists to block injection exploits, and rigorously reject bypasses on mandatory documents when strict compliance is enabled.

### Option B: Permissive Fallback to PKCS#1 v1.5 Padding
Allow deterministic PKCS#1 v1.5 verification to succeed silently (or with a warning) to maximize compatibility with legacy external integrations. This option, however, compromises strict 21 CFR Part 11 security requirements and fails to align with rigorous GxP posture, leaving the system exposed to legacy cryptographic vulnerabilities.

## 4. Decision Outcome

**Chosen option: Option A** because it satisfies the rigorous technical requirements of **Trace-13** and ensures absolute authenticity and tamper resistance of Part 11 electronic records. 

### Key Technical Enhancements:
1. **Block Injection Defenses**:
   * Modified `apps/compliance/services/esignature_verifier.py` to count and block duplicate certificate/signature markers (e.g., `-----BEGIN CERTIFICATE-----`, `-----BEGIN SIGNATURE-----`, `<SignatureValue>`, etc.). Any payload with multiple blocks is rejected with `DUPLICATE_BLOCKS_REJECTED`.
2. **Explicit Legacy Rejection & Alerts**:
   * Implemented fallback checks in `packages/security/crypto_verifier.py`, `apps/compliance/services/esignature_verifier.py`, and `apps/etmf/cryptography.py` to identify legacy PKCS#1 v1.5 signatures.
   * If PKCS#1 v1.5 succeeds where PSS fails, a high-severity `COMPLIANCE ALERT: Legacy PKCS#1 v1.5 signature padding detected. This signature is insecure and has been rejected` is emitted, and a specific rejection code (`LEGACY_PADDING_REJECTED`) is returned.
3. **Uncompromising Bypass Rejection**:
   * Refined bypass checks in `apps/etmf/cryptography.py` to reject signature bypasses on mandatory documents if either strict compliance is active OR mock signatures are disallowed.

## 5. Consequences & Trade-offs

* **Positive**:
  * High-assurance signature validation matching the expectations of GxP auditors and FDA guidelines.
  * Rapid identification of misconfigured client-side signers using deprecated padding mechanisms.
  * Prevention of complex payload manipulation/injection attacks.
* **Negative**:
  * Client applications using older signing libraries that only support PKCS#1 v1.5 must be upgraded to generate RSA-PSS signatures.

## 6. Implementation & Verification

* **Modified Source Files**:
  * `apps/compliance/services/esignature_verifier.py`: Added block injection validation and RSA-PSS legacy fallback/rejection logic.
  * `apps/etmf/cryptography.py`: Added legacy padding detection in X.509 signature verification and tightened document bypass constraints.
  * `packages/security/crypto_verifier.py`: Implemented legacy RSA-PSS fallback rejection and compliant audit logs.
* **Verification & Testing**:
  * Tests validating compliance alerts, duplicate block rejections, and PKCS#1 v1.5 signature rejections are maintained in `tests/` and mapped to trace keys under `tests/test_etmf_signing_lifecycle.py`.

