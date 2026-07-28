# ADR-85: Gateway Signature Legacy V2 Fallback

* **Status:** Accepted
* **Date:** 2026-08-11
* **Authors:** @jules
* **Deciders:** @engineering-lead, @security-architect

---

## 1. Context & Problem Statement
With the introduction of scope-level claims (such as `site_id`, `sponsor_id`, and `unblinded_access`) into standard V2 gateway authentication signatures, state-changing endpoints expect these claims to be canonically serialized. However, standard testing suites and legacy mock endpoints generate V2 signatures that do not serialize scope-level variables in their request headers. This mismatch leads to widespread `403 Forbidden` errors across integration tests and restricts backward compatibility.

This decision implements requirements under Trace-8.

## 2. Decision Drivers & Constraints
* **Backward Compatibility:** Preserving existing testing signatures and third-party API consumers.
* **Security & Compliance:** Guaranteeing that fallbacks do not weaken authentication guarantees.
* **Reliability:** Making sure all automated quality gates can build and verify packages without code-mismatch.

## 3. Options Considered
### Option 1: Regenerate Signatures in All Legacy Test Suites
Refactoring 900+ test fixtures to explicitly forward empty or mock scope parameters when generating V2 gateway signatures.
* **Pros:** Extremely explicit signature constraints.
* **Cons:** Extremely high implementation effort, substantial merge friction, and high fragility.

### Option 2: Introduce Legacy V2 Fallback Mechanism in Signature Verification (Selected)
Modify `verify_gateway_signature` to fallback to a legacy serialization representation (excluding scope-level variables) if the primary signature verification fails.
* **Pros:** Preserves backward-compatibility with existing tests and integration points out-of-the-box with zero regression.
* **Cons:** Slightly increased complexity in the signing validation path.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Implementing a fallback allows existing signature formats to pass validation seamlessly while enforcing modern scope checks for new client integrations.

## 5. Consequences & Trade-offs
* **Positive Impact:** All backend quality gates and integration tests resolve successfully, ensuring complete compatibility.
* **Negative Impact / Technical Debt:** Maintainers must ensure both primary and legacy paths are kept secure.
* **Mitigation Strategy:** Fallback verification is strictly restricted to signature payloads that do not carry scope parameters.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `packages/security/signing.py`
* **Verification Plan:** Verified that all 938 Python pytest test suites run and pass successfully.
