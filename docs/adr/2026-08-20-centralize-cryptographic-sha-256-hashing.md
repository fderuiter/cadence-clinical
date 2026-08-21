# ADR-2185: Centralize Cryptographic SHA-256 Hashing

* **Status:** Accepted
* **Date:** 2026-08-20
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Duplicate SHA-256 calculation logic was previously implemented ad-hoc across multiple compliance and security services (`packages/compliance/services/esignature_verifier.py`, `packages/compliance/services/gxp_signer.py`, `packages/security/signature_builder.py`, and `packages/security/signing.py`). This duplication created maintainability risks and triggered code duplication quality gate failures. Refactoring was needed to centralize SHA-256 computation while satisfying system security requirements (PRD-SYS-001).

## 2. Decision Drivers & Constraints

* Prevent code duplication pipeline failures across security and compliance modules.
* Ensure consistent UTF-8 string encoding and byte handling for cryptographic hashing.
* Maintain strict 21 CFR Part 11 electronic signature verification standards (PRD-SYS-001).

## 3. Options Considered

1. Option A (Selected): Centralize `compute_sha256_hash` helper in `packages.security.signing` and reuse it across all compliance and signature builder services.
2. Option B (Alternative): Keep duplicate `hashlib.sha256` logic in each module and whitelist the file pairs in the duplication scanner.

## 4. Decision Outcome

Chosen option: Option A because centralizing the SHA-256 helper in `packages.security.signing` eliminates code duplication, guarantees uniform byte serialization and encoding across all signature and compliance verification pathways, and satisfies PRD-SYS-001 while ensuring long-term system maintainability.

## 5. Consequences & Trade-offs

* Positive: Eliminates duplicated hashing routines across security and compliance modules.
* Positive: Centralizes SHA-256 string/bytes type handling in a single helper.
* Negative: Modules now depend directly on `packages.security.signing.compute_sha256_hash`.

## 6. Implementation & Verification

* Refactored `esignature_verifier.py`, `gxp_signer.py`, `signature_builder.py`, and `signing.py` to import and call `compute_sha256_hash`.
* Verified via unit tests in `packages/security/tests/` and `packages/compliance/tests/`.

