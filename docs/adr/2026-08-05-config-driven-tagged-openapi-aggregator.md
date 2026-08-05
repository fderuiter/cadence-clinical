# ADR-2159: Config-Driven Tagged OpenAPI Schema Aggregator

- **Status:** Accepted
- **Date:** 2026-08-05
- **Authors:** @google-labs-jules
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

To support regulatory compliance, standard GxP documentation, and API integrations across the monorepo, we need a unified API specification. Previously, our API contract validation and synchronization only covered `designer` and `execution` services, omitting `ctms`, `etmf`, and `quality`. Fragmented API specifications pose compliance and verification risks. 

In addition, running synchronization locally shouldn't break developer workflows if certain environment configurations or local database setups are missing. Hence, we need a declarative, offline-resilient OpenAPI schema aggregator that safely unifies schemas from all monorepo services without local compilation failures.

This decision implements requirements under Trace-3.

## 2. Decision Drivers & Constraints

- **Completeness:** Unified API schema covering all five active monorepo services (`designer`, `execution`, `ctms`, `etmf`, and `quality`).
- **Resiliency / Offline Support:** Local synchronization scripts must not crash if database migrations or environment variables are missing for any service.
- **Collision Prevention:** Avoid schema model conflicts (e.g., duplicate `AuditLogResponse` definitions across distinct microservices).
- **Service Ownership:** Clearly tag and identify routes associated with each microservice.

## 3. Options Considered

### Option 1: Static Manual Unification

- **Overview:** Manually aggregate and edit Section 7 of the API Integration Specification markdown file.
- **Pros:**
  - ✅ No complex code logic required.
- **Cons:**
  - ❌ Highly prone to human error and rapidly drifts from code.
  - ❌ Extremely tedious and scales poorly.

### Option 2: Config-Driven Programmatic Aggregator (Selected)

- **Overview:** Build a dynamic script (`scripts/sync_openapi_spec.py`) that uses a declarative service registry, dynamically imports services with robust `try-except` handling for offline compilation, recursively rewrites `$ref` schema names to avoid collisions, and tags routes dynamically.
- **Pros:**
  - ✅ Fully automated and always in sync with code.
  - ✅ Gracefully logs warnings and exits with code 0 if some services cannot compile offline.
  - ✅ Confidently prevents model collisions and preserves ownership via route tagging.
- **Cons:**
  - ❌ Initial implementation complexity.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Option 2 provides a fully automated, resilient, and robust integration mechanism that fulfills GxP compliance constraints while ensuring a friction-free experience for local development workflows.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - Continuous API contract validation across all five monorepo services.
  - Seamless developer setup without requiring all five databases to run local documentation tasks.
  - No model collisions in the final generated OpenAPI schema.
- **Negative Impact / Technical Debt:**
  - Maintenance of the custom `$ref` schema rewriter and registry configurations.

## 6. Implementation & Verification

- **Affected Repositories / Services:**
  - `scripts/sync_openapi_spec.py`
  - `docs/SDLC/03_API_Integration_Specification.md`
  - `tests/test_api_contract_validation.py`
  - `tests/test_sync_openapi_spec.py`
- **Verification Plan:**
  - **Unit Testing:** Run `pytest tests/test_sync_openapi_spec.py` to assert registry, rewriter, and tagger logic.
  - **API Validation:** Run `pytest tests/test_api_contract_validation.py` to verify unified route alignment.
  - **ADR Validation:** Validate via `python scripts/validate_adrs.py`.
