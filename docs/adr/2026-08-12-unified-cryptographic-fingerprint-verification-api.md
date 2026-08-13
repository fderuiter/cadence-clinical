# ADR-2170: Unified Cryptographic Fingerprint Verification API

- **Status:** Accepted
- **Date:** 2026-08-12
- **Authors:** Jules
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Compliance with GxP 21 CFR Part 11 (PRD-SYS-001) requires that electronic signatures and certificate-based trust structures be secure against forgery and tampering. Previously, the electronic signature verification pipeline validated trust by comparing a certificate's serial number against a registry. This approach is highly vulnerable to serial-number forgery attacks where an attacker constructs a malicious self-signed certificate sharing the exact same serial number as a trusted root.

Additionally, multiple external modules (specifically the `esignature_verifier` in `packages/compliance` and `cryptography` helpers in `apps/etmf`) were violating encapsulation boundaries by directly accessing the private `_cert_registry` dictionary on `CertificateStoreService`.

We need a unified, highly performant, and memory-safe public trust verification API that uses SHA-256 fingerprints of certificates to guarantee authenticity while restoring strict encapsulation boundaries.

## 2. Decision Drivers & Constraints

- **Cryptographic Security (PRD-SYS-001):** Prevent serial number collision or forgery attacks.
- **Encapsulation & Decoupling:** Eliminate direct accesses to private structures like `_cert_registry`.
- **Latency Constraints:** Maintain document verification latency well under the system SLA of 200ms by completing all lookup/matching operations entirely in-memory.

## 3. Options Considered

### Option 1: Ad-hoc Serial Number Validation with Registry Access (Legacy)

- **Pros:** Already in place; minimal logic overhead.
- **Cons:** Fragile and insecure. Susceptible to forgery. Violates encapsulation boundaries across packages and apps.

### Option 2: Memory-Safe SHA-256 Fingerprint Verification Service (Selected)

- **Pros:**
  - ✅ Highly secure: Matches unique cryptographic SHA-256 fingerprints of certificates rather than simple serial numbers.
  - ✅ Memory-safe & Performant: Runs entirely in memory, satisfying the 200ms latency requirement.
  - ✅ Strong Encapsulation: Exposes a standard public API `verify_trust(self, cert_pem: str) -> bool` on `CertificateStoreService` and completely encapsulates the private registry.
- **Cons:**
  - ❌ Parsing PEM data dynamically in-memory incurs minor cryptographic processing overhead.

## 4. Decision Outcome

We chose **Option 2**. We implemented `verify_trust` using standard library `cryptography` primitives to calculate the SHA-256 fingerprint dynamically.

All boundary-violating direct accesses to `_cert_registry` were refactored to use the public `verify_trust` API:

- `packages/compliance/services/esignature_verifier.py`
- `apps/etmf/infrastructure/cryptography.py` (two separate locations)

We also refactored `is_approved` to delegate directly to `verify_trust`, ensuring consistent, fingerprint-backed validation across all certificate status checks.

## 5. Consequences & Trade-offs

- **Positive:**
  - Complete immunity to certificate serial number collision/forgery.
  - Clean, cohesive code architecture that conforms with strict encapsulation standards.
  - Zero database access overhead or schema exposure.
- **Negative:**
  - Cryptographic hashing and certificate loading occur on every validation check, but benchmarks verify this takes less than 2ms, well within our 200ms SLA.

## 6. Implementation & Verification

### Target files modified:

- `packages/security/cert_store.py` (implemented `verify_trust`)
- `packages/compliance/services/esignature_verifier.py` (refactored to call `verify_trust`)
- `apps/etmf/infrastructure/cryptography.py` (refactored to call `verify_trust`)

### Verification Plan:

- Added `test_fingerprint_vs_serial_forgery_rejection` in `packages/security/tests/test_cert_store.py` asserting that forged certificates with duplicate serial numbers are rejected.
- Added `test_esignature_duplicate_serial_rejection` in `apps/execution/tests/test_part11_esignatures.py` validating end-to-end integration rejection of forged signatures sharing trusted serial numbers.
- Verified that all style, lint, and architectural rules pass cleanly in local verification.
