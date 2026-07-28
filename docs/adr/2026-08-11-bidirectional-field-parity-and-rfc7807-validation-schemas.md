# ADR-086: Bidirectional Field Parity and RFC 7807 Validation Schemas

* **Status:** Accepted
* **Date:** 2026-08-11
* **Authors:** @jules
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement
The Metadata-Driven Clinical Execution Platform handles highly structured and critical clinical trials data where strict validation is crucial. Pydantic's default FastAPI request validation returns an HTTP `422 Unprocessable Entity` with standard error formatting. This is insufficient for highly compliant clinical platform environments, which require RFC 7807 compliant error payloads (`ProblemDetails` format) to return detailed and predictable schema errors to callers, while mapping standard request validation exceptions strictly to `400 Bad Request` instead of `422`. Furthermore, we must enforce bidirectional field parity across service borders to ensure consistent, compliant data serialization and validation contracts.

This decision implements requirements under PRD-SYS-001.

## 2. Decision Drivers & Constraints
* **Driver 1:** Consistent RFC 7807 `ProblemDetails` error formatting for all validation exceptions.
* **Driver 2:** Strict HTTP status code alignment where client validation/request failures consistently return `400 Bad Request`.
* **Driver 3:** Enforcing strict clinical boundaries and field parity across service boundaries.

## 3. Options Considered
### Option 1: Default FastAPI Request Validation (HTTP 422)
* **Overview:** Rely on default FastAPI `RequestValidationError` handling.
* **Pros:**
  * ✅ No custom code required.
* **Cons:**
  * ❌ Does not conform to RFC 7807 compliant error payloads.
  * ❌ Returns `422 Unprocessable Entity` instead of `400 Bad Request`.

### Option 2: Custom Request Validation Exception Handlers mapping to RFC 7807 (Selected)
* **Overview:** Register custom FastAPI exception handlers for `RequestValidationError` inside `designer` and `execution` microservices to transform standard validation errors into a standardized `ProblemDetails` model returning HTTP 400.
* **Pros:**
  * ✅ Fully compliant with RFC 7807 and returns structured invalid parameters.
  * ✅ Ensures validation failures return uniform HTTP 400 Bad Request status.
  * ✅ Enforces strict validation schema constraints.
* **Cons:**
  * ❌ Requires updating legacy unit tests that originally expected HTTP 422 to now assert HTTP 400.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Implementing standardized RFC 7807 `ProblemDetails` schema handlers for validation errors ensures robust, compliant, and detailed feedback to API consumers. Mapping validation errors to HTTP 400 provides a unified, industry-standard contract for client-side input errors on our clinical platform.

## 5. Consequences & Trade-offs
* **Positive Impact:** API clients receive highly-detailed `ProblemDetails` messages, reducing debugging friction and reinforcing GxP compliance.
* **Negative Impact / Technical Debt:** Requires adapting existing validation tests to align with the new HTTP 400/RFC 7807 response schema.
* **Mitigation Strategy:** Update all test suites to enforce HTTP 400 assertions for malformed payload structures and validation errors.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `apps/designer`, `apps/execution`, `tests/`
* **Verification Plan:** Verify implementation using backend test suite containing explicit validations of the RFC 7807 payload structure and response status codes.
