# ADR-2159: Config-Driven Tagged OpenAPI Aggregator for Monorepo Services

- **Status:** Accepted
- **Date:** 2026-08-05
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

In the Cadence Clinical Platform, multiple microservices and monorepo packages (e.g., `apps/ctms`, `apps/designer`, `apps/execution`, `apps/gateway`, etc.) expose independent REST APIs. We need a unified API integration specification that aggregates and reconciles all downstream OpenAPI endpoints at the API Gateway level. Previously, API contract synchronization was fragile, requiring manual execution of generation scripts and lacked robust tagging/configuration rules to isolate internal, external, and service-specific routes.

This ADR describes the introduction of a config-driven tagged OpenAPI aggregator (`scripts/sync_openapi_spec.py`) that programmatically discovers, parses, validates, and slices microservice schemas using a declarative, configuration-driven tag matrix. This satisfies the platform integration requirements of PRD-SYS-001.

## 2. Decision Drivers & Constraints

- **API Consistency:** Need a single source of truth for downstream clinical operations and client applications.
- **Developer Velocity:** Automatic verification of OpenAPI compatibility across services on pre-commit and CI stages.
- **GxP Compliance & Security (PRD-SYS-001):** Ensure strict separation and filtering of public vs. internal system schemas via tagged route endpoints.

## 3. Options Considered

### Option 1: Static Offline Generation

Manual execution of fastapi export command inside each app directory.

- **Pros:**
  - ✅ Simple to implement.
- **Cons:**
  - ❌ Susceptible to human error, out-of-sync API contracts, and slow developer workflows.

### Option 2: Config-Driven Automated Aggregator (Selected)

An automated script `scripts/sync_openapi_spec.py` that utilizes a YAML or pythonic config mapping of all monorepo microservices, exports their OpenAPI specs via programmatic lifespan hooks, and aggregates them under designated OpenAPI tag namespace filters.

- **Pros:**
  - ✅ Fully automated, repeatable, and easily verified in CI/CD pipeline tests.
  - ✅ Enables declarative tagging and filtering of routing paths.
- **Cons:**
  - ❌ Requires maintaining aggregation/synchronization scripts and registering new microservice gateways in the master config.

## 4. Decision Outcome

**Chosen Option:** Option 2. We choose the programmatic Config-Driven Tagged OpenAPI Aggregator. It guarantees that our master API specifications inside `docs/SDLC/03_API_Integration_Specification.md` remain strictly synchronized with active service endpoints, and enables fail-fast validation checks to verify that any modification to public-facing API endpoints is fully compliant and traced.

## 5. Consequences & Trade-offs

- **Positive Impact:** Auto-generation and synchronization of unified api spec contracts are seamless; schema drifts are caught immediately during linter/test execution.
- **Negative Impact:** Adding a new service requires updating the aggregator configuration and register block in `conftest.py`.
- **Mitigation Strategy:** Provide detailed developer instructions and ensure tests under `tests/test_sync_openapi_spec.py` fail fast with descriptive guidance.

## 6. Implementation & Verification

- **Affected Repositories / Services:** API Gateway (`apps/gateway/`), and monorepo services (`apps/ctms/`, `apps/execution/`, etc.)
- **Verification Plan:**
  - Validate using programmatic contract validation tests (`tests/test_api_contract_validation.py`).
  - Run automated local verify command: `uv run pytest tests/test_sync_openapi_spec.py`.
