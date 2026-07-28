# ADR-0078: Dynamic Runtime Configuration Validation

* **Status:** Accepted
* **Date:** 2026-08-16
* **Authors:** @jules
* **Deciders:** Engineering Leadership, Security Compliance Committee

---

## 1. Context & Problem Statement
In automated CI/CD pipelines and deployment processes, there exists a vulnerability where microservices might accidentally boot or load under a production profile while still using default/fallback symmetric secrets (such as the default API gateway secret) or active developer bypass variables (e.g., bypassing JWT signature verification). This allows unverified, insecure configurations to deploy to live production environments, violating 21 CFR Part 11 and other regulatory compliance standards.

We need a deterministic, fail-fast mechanism that dynamically asserts that microservices refuse to boot when configured with insecure defaults or development bypass flags under a production configuration.

## 2. Decision Drivers & Constraints
* **Compliance & Security:** Must prevent active development bypasses and weak symmetric keys from ever reaching production environments.
* **Developer Friction:** Must not modify or eliminate local developer environments or their default development fallback secrets.
* **Execution Overhead:** Configuration checks must execute dynamically as standard tests without needing separate network probing or complex orchestration.
* **Dynamic Property Parsing:** Support microservices that parse environment properties individually at runtime.

## 3. Options Considered
### Option 1: Module-level Static Configuration Check on Import
* **Overview:** Validate the configuration statically at the top-level of each microservice's `main.py` when imported.
* **Pros:**
  * ✅ Extremely simple to implement.
* **Cons:**
  * ❌ Static imports might not capture runtime properties loaded after imports (violating dynamic parsing).
  * ❌ Harder to dynamically mock environments during standard automated tests without complex reloading.

### Option 2: Centralized Common Validation Utility Executed during Middleware & Entrypoint Initialization
* **Overview:** Implement a common security module `packages/security/config_validation.py` with a `validate_runtime_config()` function. Import and execute it inside `GatewayAuthMiddleware.__init__` (wrapping all downstream microservices) and at the module-level of the Gateway app (`apps/gateway/main.py`).
* **Pros:**
  * ✅ Captures both central entrypoints and downstream service bootstrapping.
  * ✅ Checks are fully dynamic, reading live environment variables at the time of instantiation.
  * ✅ Integrates seamlessly with standard test runners (pytest) by allowing clean environment mocking.
* **Cons:**
  * ❌ Minor code changes required in common libraries and key main entrypoints.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Choosing Option 2 guarantees that configuration verification is performed dynamically on boot across all active microservices (via gateway and standard middleware initialization), whilst remaining fully mockable in local test suites to maintain developer ease.

## 5. Consequences & Trade-offs
* **Positive Impact:** Fail-fast behavior stops deployment containers from fully initializing or starting up when an insecure environment configuration is detected, completely eliminating the possibility of running insecure/bypass code in production.
* **Negative Impact / Trade-offs:** None identified. No network dependencies or third-party packages are introduced.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `packages/security/`, `apps/gateway/main.py`, and any app utilizing the security middleware.
* **Verification Plan:** Verified via standard pytest suite: `tests/test_dynamic_config_assertions.py`.
