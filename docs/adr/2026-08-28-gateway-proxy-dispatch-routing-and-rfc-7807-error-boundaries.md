# ADR-2225: Gateway Proxy Dispatch Routing and RFC 7807 Error Boundaries

- **Status:** Accepted
- **Date:** 2026-08-28
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

The Cadence Clinical API Gateway (`apps/gateway`) acts as the single reverse-proxy entry point and security boundary for all downstream microservices. However, newly developed microservices—specifically Medical Imaging (`apps/imaging`, port 8014), Site Payments (`apps/payments`, port 8016), and AI Gateway (`apps/ai_gateway`, port 8017)—previously lacked dedicated proxy routing, OpenAPI schema aggregation, and CLI port discovery definitions. Furthermore, synthetic errors produced directly by the Gateway (such as 401 Unauthorized, 403 Forbidden, 429 Rate Limit Exceeded, 502 Bad Gateway, and 504 Gateway Timeout) returned legacy JSON `{ "detail": "..." }` dictionaries rather than standard RFC 7807 `application/problem+json` envelopes. This violated enterprise API consistency and GxP error boundary requirements (PRD-SYS-001, PRD-SYS-003, PRD-IMG-001, PRD-PAY-002).

## 2. Decision Drivers & Constraints

* **Proxy Route Exhaustiveness:** Every platform microservice must have deterministic prefix routing supporting both canonical `/api/v1/{service}/*` and direct `{service}/*` paths with environment variable overrides (`IMAGING_URL`, `PAYMENTS_URL`, `AI_GATEWAY_URL`).
* **RFC 7807 Error Boundaries:** All synthetic errors generated at the Gateway boundary must return structured `application/problem+json` bodies (`ProblemDetails`) with standard type URIs (`https://cadence.clinical/errors/*`), title, status, detail, and instance path.
* **Transparent Payload Streaming:** Downstream responses (including non-2xx responses already formatted as Problem Details by downstream microservices) must stream transparently without schema mutations or double-wrapping.
* **Transport Error Mapping:** Network failures contacting downstream services must be cleanly caught and mapped: connection errors/refusals map to 502 Bad Gateway, and timeouts map to 504 Gateway Timeout.
* **OpenAPI 3.1.0 Aggregation:** The Gateway `/openapi.json` endpoint must dynamically gather schemas across all 16 microservices with isolated schema component prefixes (`Imaging_`, `Payments_`, `AiGateway_`) to prevent naming collisions in the unified Swagger UI.
* **Tooling & Orchestration Alignment:** Port definitions and mappings in `packages/cli/ports.py` must register default ports for all microservices (imaging: 8014, knowledge: 8015, payments: 8016, ai_gateway: 8017).

## 3. Options Considered

### Option 1: **Option A (Selected):** Add dedicated proxy dispatch routing in `apps/gateway/main.py` for `imaging`, `payments`, and `ai_gateway`, serialize all Gateway synthetic errors via `packages.hexagonal.ProblemDetails` (`application/problem+json`), map connection errors to 502 and timeouts to 504, dynamically aggregate OpenAPI specs with component prefixes, and register service ports in `packages/cli/ports.py`.

- **Overview:** **Option A (Selected):** Add dedicated proxy dispatch routing in `apps/gateway/main.py` for `imaging`, `payments`, and `ai_gateway`, serialize all Gateway synthetic errors via `packages.hexagonal.ProblemDetails` (`application/problem+json`), map connection errors to 502 and timeouts to 504, dynamically aggregate OpenAPI specs with component prefixes, and register service ports in `packages/cli/ports.py`.
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead

### Option 2: **Option B:** Leave specialized microservices behind independent host ports and client-side direct routing (Rejected - breaks API gateway unification, single sign-on security boundaries, and HMAC gateway signature propagation).

- **Overview:** **Option B:** Leave specialized microservices behind independent host ports and client-side direct routing (Rejected - breaks API gateway unification, single sign-on security boundaries, and HMAC gateway signature propagation).
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead

### Option 3: **Option C:** Intercept and rewrite all downstream responses in the Gateway to enforce RFC 7807 (Rejected - degrades throughput and violates microservice autonomy).

- **Overview:** **Option C:** Intercept and rewrite all downstream responses in the Gateway to enforce RFC 7807 (Rejected - degrades throughput and violates microservice autonomy).
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead


## 4. Decision Outcome

- **Chosen Option:** **Option A** because it enforces a clean, secure, and fully typed API gateway architecture that satisfies PRD-SYS-001, PRD-SYS-003, PRD-IMG-001, and PRD-PAY-002 while maintaining 100% RFC 7807 compliance across platform error boundaries.
- **Justification:** Selected as primary architectural approach.

## 5. Consequences & Trade-offs

* **Positive:**
  * Unified single-endpoint access for frontend applications (`apps/web`, `apps/subject-portal`) and external integrations to all clinical services.
  * Standardized RFC 7807 error responses for client-side diagnostic handling.
  * Seamless OpenAPI schema inspection at `/docs` across the entire clinical software platform.
* **Negative:**
  * Requires maintaining service URL environment configurations and port mappings.

## 6. Implementation & Verification

* **Gateway Routing & Errors:** Updated `apps/gateway/main.py` with `SERVICES["payments"]`, `SERVICES["ai_gateway"]`, dedicated dispatch branches, RFC 7807 synthetic error helpers, and 502/504 transport exception handling.
* **OpenAPI Aggregation:** Updated `get_openapi_json()` and `custom_openapi()` in `apps/gateway/main.py` to aggregate `imaging`, `payments`, and `ai_gateway` schemas.
* **CLI Port Management:** Registered `payments` (8016) and `ai_gateway` (8017) in `packages/cli/ports.py`.
* **Automated Test Coverage:** Author unit and integration tests in `apps/gateway/tests/test_gateway.py` verifying proxy routing, RFC 7807 problem details payloads, 502/504 error mappings, and OpenAPI aggregation.
* **GxP Traceability:** Verify via `uv run cadence check --parallel` and sync compliance matrix via `uv run python scripts/sync_gxp.py`.

