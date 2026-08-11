# ADR-[NUMBER]: Persistent Local PIN Wrapper and Secure Offline IndexedDB Encryption

- **Status:** Accepted
- **Date:** 2026-08-11
- **Authors:** @jules
- **Deciders:** @jules, @fderuiter

---

## 1. Context & Problem Statement

When internet connectivity is lost, the eCOA Subject Portal (`apps/subject-portal`) transitions seamlessly into Offline Capture Mode. During this time, user inputs and clinical diary data must be preserved securely in local browser storage. Per **PRD-EDC-007**, this data must be encrypted locally in IndexedDB using AES-GCM. 
To achieve secure key-wrapping, key derivation, and multi-user isolation on shared devices without exposing raw master keys or session keys to local storage, we require a persistent local PIN wrapper mechanism.

## 2. Decision Drivers & Constraints

- **Compliance:** Support 21 CFR Part 11 and EU Annex 11 requirements for local data security and transaction non-repudiation (**Trace-9**).
- **Security:** Ensure zero plain-text storage of master keys or session keys in browser storage (**PRD-EDC-007**).
- **Usability:** Allow subjects to unlock and decrypt their offline queue using a user-supplied 4-digit PIN, even across browser reloads or network restarts.
- **Robustness:** Resolve synchronization and conflict states on reconnection while guaranteeing data integrity (**PRD-EDC-008**).

## 3. Options Considered

### Option 1: Plaintext Local Key Storage

Store the derived AES-GCM session key directly in local storage.
- **Pros:**
  - Simple to implement and requires no PIN interaction.
- **Cons:**
  - ❌ Fails to satisfy security requirements under GxP / Part 11.
  - ❌ Allows any unauthorized user or extension with local storage access to decrypt patient records.

### Option 2: PIN-Wrapped Persistent Local Key Storage (Selected)

Leverage a 4-digit user PIN to wrap the cryptographic master key, storing only the wrapped key and verification salt in browser persistent storage.
- **Pros:**
  - ✅ Securely derives the decryption key from PIN on-demand.
  - ✅ Eliminates the risk of plain-text cryptographic keys being exposed in persistent storage.
  - ✅ Correctly implements local IndexedDB security in alignment with **PRD-EDC-007**.
- **Cons:**
  - Requires user interaction to supply the PIN to unlock the sync queue.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Choosing Option 2 guarantees compliance with **PRD-EDC-007** and **Trace-9** requirements. The cryptographic master key is wrapped and securely persisted, and the active session key is maintained purely in-memory.

## 5. Consequences & Trade-offs

- **Positive Impact:** Full compliance with 21 CFR Part 11 and EU Annex 11 security guidelines. Strong data security on shared patient devices.
- **Negative Impact / Technical Debt:** Added client-side cryptographic overhead during PIN validation and key derivation.
- **Mitigation Strategy:** Leverage Web Crypto API native primitives for fast, standardized PBKDF2/AES-GCM encryption on modern mobile and desktop browsers.

## 6. Implementation & Verification

- **Affected Repositories / Services:**
  - `apps/subject-portal`
  - `packages/ui`
- **Verification Plan:**
  - Verified via ESLint / `make lint` cleanups to prevent style regressions.
  - Verified via frontend unit and integration tests (`pnpm -r test`) ensuring `pin-wrapper.test.js` covers both success and failure execution branches.
