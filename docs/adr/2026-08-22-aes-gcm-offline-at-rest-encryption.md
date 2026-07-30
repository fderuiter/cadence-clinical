# ADR-099: Shared AES-GCM Offline At-Rest Encryption and Key Derivation Contract

* **Status:** Accepted
* **Date:** 2026-08-22
* **Authors:** @jules
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement
Pursuant to **FDA 21 CFR Part 11** and **PRD-EDC-007**, patient-entered eCOA diary and survey answers must be encrypted at rest in local IndexedDB storage during disconnected/offline mode. Plaintext exposure of clinical observations or patient pseudonymized identifiers at-rest poses data privacy and regulatory compliance risks.

## 2. Decision Drivers & Constraints
* **Driver 1:** Secure at-rest storage for disconnected ePRO captures using standardized, byte-exact AES-GCM.
* **Driver 2:** Deterministic, cross-language cryptographic compatibility between browser-side Web Crypto and server-side Python libraries.
* **Driver 3:** No persistent storage of raw Keycloak tokens or raw symmetric keys.

## 3. Options Considered
### Option 1: AES-CBC or Fernet
* **Overview:** Standard AES block ciphers.
* **Cons:** Lack of built-in authenticated data (AAD) support; Fernet has no native Web Crypto subtle API equivalent in standard browsers without massive third-party packages.

### Option 2: AES-GCM (Selected)
* **Overview:** Authenticated encryption with associated data (AEAD) using Galois/Counter Mode.
* **Pros:**
  * ✅ Universally supported by browser Web Crypto and Python `cryptography` libraries.
  * ✅ High performance and hardware acceleration.
  * ✅ Built-in authenticity verification (tags) and AAD binding.

## 4. Decision Outcome
* **Chosen Option:** Option 2 (AES-GCM)
* **Justification:** Chosen for native compatibility, high security, and perfect suitability for browser-based offline capture.

## 5. Consequences & Trade-offs
* **Positive Impact:** Answers are protected at rest; tamper attempts are immediately rejected on decryption.
* **Negative Impact:** Key derivation and encryption/decryption are asynchronous, requiring split transactions in IndexedDB.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `packages/security`, `packages/ui`, `apps/subject-portal`, `apps/interop`
* **Verification Plan:** Verified through pytest and vitest suites verifying byte-identical decryption across languages.
