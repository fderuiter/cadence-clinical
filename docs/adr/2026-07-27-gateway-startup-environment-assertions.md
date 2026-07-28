# ADR-096: API Gateway Startup Environment Assertions to Prevent Production Auth Bypasses

* Status: Accepted
* Date: 2026-07-27
* Authors: @jules
* Deciders: @fderuiter

---

## 1. Context & Problem Statement
To maintain strict GxP and FDA 21 CFR Part 11 compliance, the API Gateway must guarantee that critical security mechanisms—specifically JWT token verification and digital signature checks—cannot be bypassed in a production or staging environment.

Previously, test bypass configuration flags could be accidentally propagated to production without any automated guardrails to prevent them from executing. To eliminate the risk of accidental authentication bypasses, we need "fail-fast" programmatic environment checks at the startup phase.

This decision implements requirements under Trace-8.

## 2. Decision Drivers & Constraints
* **GxP & Security Compliance:** Zero tolerance for authentication/authorization bypasses in non-development environments.
* **Resilience & Safe Startup:** The gateway must immediately fail to boot ("fail-fast") if misconfigurations are present, ensuring vulnerable states are never exposed on network ports.
* **Developer Velocity:** Preserving quick local developer feedback loops and test velocities without manual mock-heavy overheads in local development/test modes.

## 3. Options Considered
### Option 1: Runtime validation checks on every incoming request
* **Overview:** Verify configuration state during request proxying or auth verification at runtime.
* **Pros:**
  * ✅ Catches misconfigurations when a request actually arrives.
* **Cons:**
  * ❌ Severe performance overhead.
  * ❌ Vulnerability window exists: the port is open and listening before any check runs.

### Option 2: Build-time / CI-only validation checks
* **Overview:** Check variables during build pipeline or CI execution.
* **Pros:**
  * ✅ No runtime overhead.
* **Cons:**
  * ❌ Unable to protect against dynamic environment variable misconfigurations in target containers at deployment/runtime.

### Option 3: Immediate Startup Verification and Assertions ("Fail-Fast" Boot Check) (Selected)
* **Overview:** Implement direct validation of environment variables at the module-loading stage of `apps/gateway/main.py`. If a non-development environment (`APP_ENV` not in `development`, `dev`, `test`) is active and any test bypass variables (`JWT_TEST_SECRET`, `ALLOW_UNVERIFIED_JWT_FOR_TEST`, `SKIP_JWKS_FETCH`) are set, log a clear security alert and terminate the process immediately via `sys.exit(1)`.
* **Pros:**
  * ✅ Absolute prevention of accidental production authorization bypass.
  * ✅ Eliminates vulnerability windows as the gateway crashes before binding to the port.
  * ✅ Preserves developer velocity by allowing standard bypass flags in development/test.
* **Cons:**
  * ❌ Hard crashes of container if environment is improperly configured (which is desired for security).

## 4. Decision Outcome
* **Chosen Option:** Option 3 (Immediate Startup Verification and Assertions)
* **Justification:** Option 3 provides the ultimate security guardrail by blocking vulnerable startup configurations. It guarantees that bypass parameters cannot be active in staging/production environments, and handles it cleanly before network exposure.

## 5. Consequences & Trade-offs
* **Positive Impact:** Accidental environment misconfiguration in staging/production is completely prevented from starting up.
* **Negative Impact / Technical Debt:** Requires careful configuration of deployment charts / system environments.
* **Mitigation Strategy:** Provide detailed and actionable error logs on `sys.stderr` when a startup failure is triggered.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `apps/gateway/main.py`
* **Verification Plan:** Validated via automated integration tests in `tests/test_gateway.py` checking successful and failed boot triggers under different `APP_ENV` values.
