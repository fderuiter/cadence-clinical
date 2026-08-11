# ADR-2169: Offline and Synchronous OpenAPI Schema Generation for 15 Platform Services

- **Status:** Accepted
- **Date:** 2026-08-11
- **Authors:** @jules
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

To provide unified, interactive API documentation across the entire standalone eClinical platform, the API gateway dynamically aggregates OpenAPI schemas from downstream microservices under **Trace-8**. Previously, this aggregation required active network connections and asynchronous introspection of running services, which introduced the risk of gateway timeouts, service start order race conditions, and transient build pipeline failures.

Furthermore, running schema compilation in restricted non-production environments often failed due to missing configuration or database check prerequisites. We need a synchronous, hermetic offline OpenAPI aggregation approach that can safely merge schemas for all 15 active platform microservices at build time without active network dependencies.

## 2. Decision Drivers & Constraints

- **Hermetic Build Stability (Trace-8):** The aggregated OpenAPI contract must be compile-capable completely offline, with zero active runtime dependencies or container connections.
- **Preloaded Safe Environments:** Prevent configuration failures during downstream module imports by pre-populating dummy env placeholders before service invocation.
- **Performance & Cycle Isolation:** Downstream reference-rewriting and path prefixing must prevent naming collisions and break circular reference paths dynamically.

## 3. Options Considered

### Option 1: Live Asynchronous Aggregation in Runtime Only

- **Overview:** Fetch schemas asynchronously from active downstream services only when hitting the gateway `/openapi.json` endpoint.
- **Pros:**
  - Simple, real-time updates of schemas.
- **Cons:**
  - ❌ High latency and coupling in containerized environments.
  - ❌ Subject to startup race conditions and network instability.

### Option 2: Offline & Synchronous Pre-generation with Safe Preloads (Selected)

- **Overview:** Customize `app.openapi` in `apps/gateway/main.py` to synchronously load and merge all 12 downstream platform microservices and the 3 native gateway services offline, backed by preloaded safe dummy environment variables (`AUDIT_LOG_SECRET_KEY`, `INBOUND_EMAIL_HMAC_SECRET`, `GATEWAY_SECRET`).
- **Pros:**
  - ✅ 100% reliable, offline-capable verification pipeline.
  - ✅ Ensures API contracts are guaranteed build-time safe and validated before deployment.
- **Cons:**
  - ❌ Minor initial execution overhead when importing modules.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Option 2 perfectly satisfies **Trace-8** by providing a unified, fully offline-validatable platform OpenAPI contract that eliminates runtime dependency coupling and prevents pipeline build crashes.

## 5. Consequences & Trade-offs

- **Positive Impact:** Single source of truth OpenAPI schema generated hermetically.
- **Negative Impact / Technical Debt:** Requires importing active service apps which execute top-level imports, requiring the preloaded safe environment variables.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/gateway/main.py`
- **Verification Plan:** Verified using the static linter `validate_adrs.py` and validating schema exports via contract checking test suites.
