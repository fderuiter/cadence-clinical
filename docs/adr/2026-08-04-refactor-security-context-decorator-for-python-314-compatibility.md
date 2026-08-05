# ADR-256: Refactor security context decorator for Python 3.14 compatibility

- **Status:** Accepted
- **Date:** 2026-08-04
- **Authors:** @google-labs-jules[bot]
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

The system security and audit logging layers rely on `packages/security/context.py` to establish context vars and audit trails during API execution. Inside `audit_context_decorator`, `asyncio.iscoroutinefunction` was previously used to inspect the target function and decide whether to return a synchronous or asynchronous wrapper.

In Python 3.14+, `asyncio.iscoroutinefunction` is deprecated/removed and will trigger runtime failures. Therefore, we must migrate to a robust, future-proof mechanism to ensure standard compliance and seamless Python 3.14 runtime compatibility under PRD-SYS-001.

## 2. Decision Drivers & Constraints

- **Standard Library Best Practices:** Alignment with Python 3.14+ deprecations.
- **Maintainability & Zero Runtime Overhead:** No extra dependencies, keeping execution of the audit context layer highly performant.
- **GxP Compliance & Traceability (PRD-SYS-001):** Keep all security and audit context wrapping behavior exactly equivalent without risking runtime crashes.

## 3. Options Considered

### Option 1: Use `inspect.iscoroutinefunction` (Selected)

- **Overview:** Swap `asyncio.iscoroutinefunction` with the non-deprecated standard library `inspect.iscoroutinefunction` at the top-level module scope.
- **Pros:**
  - ✅ Standard, clean, and robust alternative supported across Python versions.
  - ✅ Avoids inline imports of `asyncio`.
  - ✅ Fully PEP-8 compliant.
- **Cons:**
  - None.

### Option 2: Wrap with try/except fallback

- **Overview:** Check for `asyncio.iscoroutinefunction` and fallback to `inspect` or similar check.
- **Pros:**
  - ✅ Backward compatible with very old Python runtimes.
- **Cons:**
  - ❌ Unnecessary complexity since the workspace environment is standardizing on Python 3.14.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** `inspect.iscoroutinefunction` is the correct standard library replacement that resolves the deprecation warning without introducing any structural risk.

## 5. Consequences & Trade-offs

- **Positive Impact:** Completely eliminates deprecation warnings and future-proofs the codebase for Python 3.14+ execution.
- **Negative Impact / Technical Debt:** None.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/security/`
- **Verification Plan:** Verified via `tests/test_security_middleware.py`. All 41 tests pass cleanly without any deprecation warnings.
