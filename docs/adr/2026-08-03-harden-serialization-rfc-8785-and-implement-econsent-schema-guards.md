# ADR-254: Harden Serialization (RFC 8785) and Implement eConsent Schema Guards

- **Status:** Accepted
- **Date:** 2026-08-03
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

To maintain strict compliance with FDA 21 CFR Part 11 and GxP guidelines, our electronic consent and ledger engine requires guaranteed data integrity and immutable, cross-runtime-consistent audit trails. Previously, the system suffered from three main categories of vulnerabilities and brittleness:

1. **Cryptographic verification mismatches**: Standard `JSON.stringify` serialization is non-deterministic across different JavaScript engines and runtimes because object key ordering is not guaranteed. Additionally, standard stringification failed to handle `Date` objects deterministically and did not cleanly handle `undefined` values inside nested structures, causing mismatching payload hashes.
2. **Brittle custom validations**: Custom AST rule evaluations strictly checked boolean mismatches (`isOk === false`), letting falsy invalid values (like `null`, `0`, or empty strings) bypass validation and get accepted as valid inputs.
3. **Application crashes on translation templates**: Missing optional metadata fields or null values in clinical translation/consent files could trigger unhandled exceptions in the frontend rendering pipeline.

This decision addresses these issues purely within the client-side helper libraries in `packages/ui`, ensuring robust defense-in-depth, strict regulatory compliance, and a performance-focused execution time of under 15ms per document load.

This decision implements and traces requirement: **PRD-SYS-001**.

---

## 2. Decision Drivers & Constraints

- **Compliance (PRD-SYS-001):** Cryptographic ledger signatures must be fully deterministic across all browsers and environments.
- **Integrity and Security:** Constraint validations must fail-closed on any invalid input (including all falsy values) instead of failing-open.
- **Fault Tolerance:** Frontend eConsent rendering must gracefully handle incomplete template payloads with defensive fallbacks without crashing the user interface.
- **Performance Budget:** Client-side structural parsing must execute in under 15ms to ensure smooth and responsive participant interactions.

---

## 3. Options Considered

### Option A: Shared Client-Side Hardened Helpers with Defensive Fallbacks (Selected)

Harden canonical serialization, AST constraint checks, and eConsent template normalizing directly inside `packages/ui` helpers (`signing.js` and `econsent.js`), with automated test suites verifying correctness.

- **Pros:**
  - ✅ Full compliance with RFC 8785 JSON Canonicalization Scheme (JCS) for Date objects and undefined properties.
  - ✅ Centralized and deterministic ledger block payload generation in `buildLedgerBlock`.
  - ✅ Robust fail-closed validation of AST criteria.
  - ✅ Defensive routing and rendering of incomplete eConsent forms.
- **Cons:**
  - ❌ Requires maintaining JS-specific validation behaviors in synchronization with Python-backend logic.

### Option B: Backend-Only Strict Pre-validation

Reject all slightly incomplete eConsent templates and non-canonical payloads directly at the API Gateway or database boundary.

- **Pros:**
  - ✅ Keeps client-side libraries extremely thin.
- **Cons:**
  - ❌ Does not prevent client-side JS runtime crashes from unexpected nested nulls or field omissions.
  - ❌ Does not resolve non-deterministic serialization on the frontend where ledger block hashes are originally generated.

---

## 4. Decision Outcome

**Chosen option: Option A** because it solves data serialization and UI robustness exactly where the data is loaded and signed.

### Detailed Technical Implementations:

1. **Deterministic Canonicalization (RFC 8785) in `packages/ui/signing.js`**:
   - `Date` objects are serialized using their standard `.toJSON()` ISO format.
   - Key-value pairs containing `undefined` values inside objects are completely omitted.
   - `undefined` elements inside arrays are serialized to `"null"` to align with JCS standards.
   - `buildLedgerBlock` uses `canonicalSerialize(details)` instead of standard `JSON.stringify(details)` to format block details prior to hashing.

2. **Robust AST Constraint Validation in `packages/ui/signing.js`**:
   - `validateField` evaluates field constraints via a truthy check (`!isOk`).
   - Any falsy evaluation result (including `false`, `null`, `undefined`, `0`, or `""`) is treated as a validation failure.

3. **Defensive eConsent Template Parsing in `packages/ui/econsent.js`**:
   - `normalizeApprovedConsent` safe-guards against null clauses and null workflow steps during iteration.
   - Supplies comprehensive default fallbacks for missing template metadata (e.g. mapping missing strings to `""` and missing `version_index` to `null`).
   - Flexible routing evaluates both `type` and `step_type` when determining the correct template workflow step.

---

## 5. Consequences & Trade-offs

- **Positive:** Complete elimination of cryptographic hash verification mismatches and frontend rendering crashes.
- **Positive:** Enhanced regulatory alignment under 21 CFR Part 11 and GxP standards.
- **Positive:** High performance remains well below the 15ms threshold.
- **Negative:** Slightly increased serialization complexity in client-side code.

---

## 6. Implementation & Verification

### Target files modified:

- `packages/ui/econsent.js` (Normalizing approved consent with fallback defaults and step types)
- `packages/ui/signing.js` (Hardened RFC 8785 serialization, truthy validation checks, deterministic ledger blocks)

### Verification tests added:

- `packages/ui/tests/econsent_utils.test.js` (Defensive normalization fallbacks and step types parsing)
- `packages/ui/tests/signing.test.js` (RFC 8785 hardening, falsy constraint checking, buildLedgerBlock signature parity)
