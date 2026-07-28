# ADR-058: Route terminology APIs through the gateway

* **Status:** Accepted
* **Date:** 2026-08-07
* **Authors:** @jules
* **Deciders:** @fderuiter, @jules

---

## 1. Context & Problem Statement
The Metadata Designer microservice exposes controlled terminology validation and search endpoints at `/api/v1/terminology`.
To allow downstream clients and external services to securely consume these terminology services, they must be routed through the unified API Gateway.
At the same time, we must preserve GxP security headers, signed identity headers, and maintain existing study-scoped routing precedence.

This decision implements requirements under Trace-8.

## 2. Decision Drivers & Constraints
* **Unified Entrypoint:** Direct clients should always interact with the API Gateway instead of hitting microservices directly.
* **Security & Auditing:** The gateway must enforce valid token verification and inject correct downstream headers, including `X-User-Id`, `X-User-Roles`, `X-Gateway-Timestamp`, `X-Gateway-Signature`, and `X-Signature-Version`.
* **Router Precedence:** Existing studies route prefix `/api/v1/studies` must remain reachable and untouched to keep study-scoped validation paths working seamlessly.

## 3. Options Considered
### Option 1: Route directly via explicit gateway rules
* **Overview:** Add explicit routing rules in `apps/gateway/main.py` mapping `api/v1/terminology` and `terminology/` path prefixes directly to `SERVICES['designer']`.
* **Pros:**
  * ✅ Highly explicit and robust routing matching existing platform patterns.
  * ✅ Preserves signed identity propagation and audit contexts perfectly.
  * ✅ Maintains existing route precedence.
* **Cons:**
  * ❌ Requires code modification in the main gateway routing logic.

### Option 2: Route as part of a generic wildcard
* **Overview:** Rely on fallback wildcard proxy routing in the gateway.
* **Pros:**
  * ✅ No direct gateway code edits needed.
* **Cons:**
  * ❌ Wildcard fallbacks are implicit and highly prone to regression if route configurations in other services overlap or change.
  * ❌ Lacks explicit verification of routing behavior in tests.

## 4. Decision Outcome
* **Chosen Option:** Option 1
* **Justification:** Option 1 guarantees high reliability, explicit test coverage, and strict enforcement of the expected routing destination.

## 5. Consequences & Trade-offs
* **Positive Impact:**
  * Terminology validation and search paths route perfectly to the Metadata Designer.
  * Fully compliant digital signatures and Part 11 auditing propagation are enforced.
* **Negative Impact / Technical Debt:**
  * None identified.

## 6. Implementation & Verification
* **Affected Repositories / Services:**
  * `apps/gateway/main.py`
  * `tests/test_gateway.py`
* **Verification Plan:**
  * **Unit Tests:** Execute `uv run pytest tests/test_gateway.py` to verify routing targets and header presence.
