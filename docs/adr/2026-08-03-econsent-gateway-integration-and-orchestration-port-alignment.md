# ADR-254: eConsent Gateway Integration and Orchestration Port Alignment

- **Status:** Accepted
- **Date:** 2026-08-03
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Clinical developers and integration engineers faced friction because the eConsent API was missing from the central documentation catalog (the Gateway's aggregated OpenAPI spec). Additionally, multi-site clinical demonstrations and local testing experienced port conflicts and routing failures due to overlapping host port mappings and internal ports (such as the CTMS port overlapping with Quality, and Org port overlapping with eISF).

To eliminate these routing failures and documentation gaps, we need to cleanly integrate the eConsent service into the API gateway, expose its OpenAPI schemas dynamically under the `Econsent_` namespace, and realign the local container port assignments to completely eliminate port conflicts while maintaining direct microservice accessibility for local developer testing.

This decision implements and traces back to requirement **PRD-SYS-001**.

## 2. Decision Drivers & Constraints

- **Driver 1 (Documentation and Discoverability):** Aggregate all downstream clinical services (including the new eConsent service) at the Gateway OpenAPI schema endpoint (`/openapi.json`) to provide a single, unified developer portal catalog.
- **Driver 2 (Port Collision Resolution):** Resolve host port overlaps of existing services (CTMS and Org) and the new eConsent service, ensuring robust containerized execution without `address already in use` or routing failures.
- **Driver 3 (Direct Testing Workflows):** Keep downstream services accessible via host ports to preserve direct validation and local integration test workflows.

## 3. Options Considered

### Option A: Gateway Schema Aggregation and Orchestration Port Realignment (Selected)

- **Overview:**
  - Modify the gateway (`apps/gateway/main.py`) to dynamically query, validate, prefix, and merge eConsent OpenAPI specs under the `Econsent_` namespace prefix.
  - Map gateway paths `/econsent` and `/api/v1/econsent` to the `econsent` container.
  - Realign port assignments to preserve 1:1 host-to-container parity:
    - **eConsent**: Host and internal ports mapped to `8011:8011`.
    - **CTMS**: Realigned to use host and internal ports `8007:8007`.
    - **Org**: Realigned to use host and internal ports `8012:8012` (completely avoiding the internal `8010` collision with eISF).
  - Update gateway URLs in environment configurations to route cleanly.
- **Pros:**
  - ✅ Simple, transparent port-to-service mapping (host port matches container port).
  - ✅ All endpoints are fully discoverable through the central Gateway.
  - ✅ Eliminates port collision risks permanently.
- **Cons:**
  - ❌ Local configuration files and environment vars referencing old ports must be updated.

### Option B: Keep Internal Container Ports with Static Mapping

- **Overview:** Keep services running on non-matching internal container ports and only redirect via host-to-container mapping (e.g. `8012:8010` for `org` or `8007:8005` for `ctms`).
- **Pros:**
  - ✅ Minimizes changes to internal service container commands.
- **Cons:**
  - ❌ Causes significant confusion during debugging when a developer calls a container internally vs calling it from the host.
  - ❌ Increases configuration maintenance complexity.

## 4. Decision Outcome

- **Chosen Option:** Option A
- **Justification:** Option A completely aligns external and internal port mappings (e.g., `8007:8007` for CTMS, `8012:8012` for Org, `8011:8011` for eConsent), eliminating port conflict risks and keeping routing settings transparent across local compose layers, while enriching the aggregated API Gateway OpenAPI specification with eConsent schema definitions.

## 5. Consequences & Trade-offs

- **Positive Impact:** Transparent port mappings across the orchestration ecosystem. Full discoverability of the eConsent API from `/openapi.json` at the gateway level.
- **Negative Impact / Technical Debt:** Requires updating onboarding documentation and any external scripts that depend on old static ports.
- **Mitigation Strategy:** Extensively updated local development manuals (`docs/LOCAL_DEV_ENVIRONMENT.md`) and aligned the local port checker utility (`scripts/check_ports.py`).

## 6. Implementation & Verification

- **Affected Repositories / Services:**
  - `apps/gateway/main.py` (added dynamic eConsent spec pulling, schema merging, and reverse-proxy routing)
  - `docker/docker-compose.yml` (added econsent container on 8011, realigned ctms on 8007, realigned org on 8012)
  - `scripts/check_ports.py` (updated mappings and checks to include eConsent and align Org/CTMS)
  - `tests/test_gateway.py` (verified econsent openapi paths and schema namespaces are merged correctly)
- **Verification Plan:**
  - Verified schema merging, namespace rewriting, and proxy routing by running `uv run pytest tests/test_gateway.py --no-cov`.
  - Verified port diagnostics using `scripts/check_ports.py`.
