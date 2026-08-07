# ADR-2161: Parameterized Branding and Dynamic Domain Configurations

- **Status:** Accepted
- **Date:** 2026-08-06
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Previously, the platform's brand identity and domain configurations were hardcoded across multiple UI clients, notification engines, and export templates. Any rebrand or domain shift required locating, modifying, and re-compiling static assets across several repositories, which was error-prone and operationally intensive.

To resolve this fragmentation, we need to introduce platform-wide white-labeling and domain injection driven entirely by deployment-level environment settings. By moving hardcoded properties to build-time and runtime parameters, we satisfy requirement (PRD-SYS-001) for dynamic branding without operational overhead.

## 2. Decision Drivers & Constraints

- **Operational Overhead:** Rebranding deployment must be accomplished via simple configuration changes without requiring multi-day engineering modifications.
- **Security & Authentication:** Dynamic domain configuration is critical for OAuth2/OIDC redirects (Keycloak) and secure internal cross-service API routing.
- **Fail-Fast Compliance:** GxP standards require that incorrect or default branding values in production trigger immediate, explicit failures rather than silent, invalid behaviors.
- **Zero Database Contamination (PRD-SYS-001):** We must keep relational database schemas unmodified and isolate external routing properties from internal container communications.

## 3. Options Considered

1. **Option A (Selected): Environment-Driven Parameters with Fail-Fast Validation**
   - Load all branding strings, Keycloak configurations, and primary domain variables strictly via runtime and build-time environment variables (`BRAND_NAME`, `BRAND_DOMAIN`, `KEYCLOAK_REALM`, etc.).
   - Implement fail-fast startup assertions in the API Gateway and microservices.
   - Inject variables into web app bundle builds dynamically.
2. **Option B: Database-Backed Branding Table**
   - Store branding parameters in a shared database schema.
   - _Drawback:_ Introduces a database dependency for startup, requires complex caching/synchronization across all services, and complicates local developer offline-mode setup.

## 4. Decision Outcome

**Chosen option: Option A** because it isolates branding configurations from database state, preserves schema integrity, and allows independent environment configuration.

Specifically:

- **Fail-Fast Startup Hooks:** Replaced hardcoded "Cadence" references with environment-driven variables in the API Gateway and microservices, and added a startup assertion (`validate_branding_and_auth()`) which halts boot in production if crucial configs are default/missing.
- **Dynamic Frontend Injection:** Leveraged Vite environment variable injection (`import.meta.env` and `%VITE_APP_TITLE%` HTML transform fallbacks) in `apps/web` and `apps/subject-portal`.
- **Configurable Webhook Headers:** Transitioned from a hardcoded `X-Cadence-Signature` signature header to a configurable `WEBHOOK_SIGNATURE_HEADER` parameter with safe default fallbacks.

## 5. Consequences & Trade-offs

- **Positive:**
  - Clean, automated rebranding driven by single deployment config changes.
  - Robust, secure OAuth2 and redirect endpoint resolution.
  - Reduced deployment verification time with immediate feedback on misconfigurations.
- **Negative:**
  - Requires correct environment variables setup in production CI/CD Helm charts or docker-compose manifests.

## 6. Implementation & Verification

- **Modified files:**
  - `apps/gateway/main.py`
  - `apps/notifications/main.py`
  - `apps/web/vite.config.js`
  - `apps/subject-portal/vite.config.js`
  - All downstream microservice `main.py` files.
- **Verification:**
  - Executed `pnpm check` locally and verified all static and validation checkers succeed perfectly.
  - Verified unit tests for routing assertions and gateway responses.
