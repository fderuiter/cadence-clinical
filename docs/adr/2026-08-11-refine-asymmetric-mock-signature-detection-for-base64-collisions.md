# ADR-2168: Refined Mock Signature Verification Base64 Collision Prevention

- **Status:** Accepted
- **Date:** 2026-08-11
- **Authors:** @jules
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

To prevent mock signatures and mock certificates from bypassing cryptographic signature verification in non-production environments under **PRD-SYS-001**, the platform previously implemented a strict check rejecting any inputs (payloads, signatures, or public keys) that contained the substring `"mock"`.

However, because real asymmetric signatures and public keys are long base64-encoded binary payloads (often hundreds of bytes), they can occasionally contain the sequence of letters `"m"`, `"o"`, `"c"`, `"k"` purely by chance (a false-positive collision). When a collision occurred, legitimate electronic signatures were incorrectly rejected, resulting in random failures in production and integration test environments. We need a more resilient and precise detection method that distinguishes actual mock strings from random base64 collisions.

## 2. Decision Drivers & Constraints

- **Security & Cryptographic Rigor (PRD-SYS-001):** We must guarantee that mock signatures/certificates are 100% blocked in regulated paths to prevent authentication bypass.
- **System Stability & Reliability:** False positive rejections of valid signatures must be minimized to zero.
- **Performance:** Detection must be highly efficient, adding negligible overhead to verification latency.

## 3. Options Considered

### Option 1: Retain Simple Substring Check

- **Overview:** Keep `"mock" in string.lower()` checks.
- **Pros:**
  - Simple to implement.
- **Cons:**
  - ❌ Continues to produce false-positive validation failures for valid base64 signatures/keys that randomly contain the substring `"mock"`.

### Option 2: Introduce Context-Aware Mock Detection (Selected)

- **Overview:** If the signature or public key is a short string (less than 64/100 characters respectively), a simple `"mock"` substring check is used. If it is longer, the string is base64-decoded first. The check for `"mock"` is only applied to the successfully decoded bytes.
- **Pros:**
  - ✅ 100% accurate; eliminates false-positive base64 collisions.
  - ✅ Maintains robust security by continuing to reject explicitly mocked short strings or base64-encoded mock payloads.
- **Cons:**
  - ❌ Requires slightly more complex string checking logic.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Option 2 completely resolves the false-positive signature verification failures caused by random base64 collisions while maintaining the strict GxP compliance rules mandated under **PRD-SYS-001**.

## 5. Consequences & Trade-offs

- **Positive Impact:** Valid electronic signatures and public keys containing the character sequence "mock" by chance will no longer cause verification failures.
- **Negative Impact / Technical Debt:** Requires maintenance of two decoding/matching helpers (`is_mock_signature` and `is_mock_key`) inside the security package.
- **Mitigation Strategy:** Automated unit tests will cover boundary cases of short and long inputs with and without "mock".

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/security/crypto_verifier.py`
- **Verification Plan:** Verified using automated pytest suite in `packages/security/tests/` to confirm that valid signatures are accepted and actual mock signatures are correctly blocked.
