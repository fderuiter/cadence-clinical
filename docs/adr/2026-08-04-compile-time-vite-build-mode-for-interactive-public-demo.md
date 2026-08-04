# ADR-253: Compile-Time Vite Build Mode for Interactive Public Demo

* **Status:** Accepted
* **Date:** 2026-08-04
* **Authors:** @jules
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement
The clinical platform must adhere to strict production compliance and lock down securely when identity providers (Keycloak) are offline. However, this compliance lockdown makes it impossible for developers, prospective clients, and users to interact with a public demo or sandbox environments without first running a local OIDC identity server. 

Requirements: PRD-SYS-001

## 2. Decision Drivers & Constraints
* **Platform Security (Production):** Under no circumstances should standard production build security matrices be altered or bypassed at runtime.
* **Ease of Access for Public Demos:** Sandbox environments must load and run seamlessly without an operational Keycloak instance.
* **Separation of Concerns & Tree-Shaking:** Ensure any demo logic is entirely tree-shaken and omitted from the standard compliance-validated production builds.

## 3. Options Considered
### Option 1: Runtime Domain / Host Header Bypasses
* **Overview:** Dynamically check the host header or location at runtime and fall back to mock credentials if running on certain public hosting subdomains.
* **Pros:**
  * ✅ Simplifies build configuration as only one build output is needed.
* **Cons:**
  * ❌ Severe GxP violation: introduces security bypass code directly into production runtimes.
  * ❌ Increases surface area of vulnerability if hostheaders are spoofed.

### Option 2: Compile-Time isolated Build Mode for Demo [Selected]
* **Overview:** Build a dedicated bundle using a dedicated Vite compile-time mode (`demo`) which completely isolates the mock seeding and OIDC check bypass.
* **Pros:**
  * ✅ Standard production builds evaluate `import.meta.env.MODE === "demo"` to false and standard production files are tree-shaken securely.
  * ✅ Interactive sandboxing works instantly without configuring or communicating with an authentication provider.
  * ✅ Allows public hosting of zero-setup interactive sandboxes cleanly.
* **Cons:**
  * ❌ Requires maintaining an additional compile command (`build:demo`) in package configs.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Choosing compile-time isolation ensures standard production systems remain 100% locked-down and compliant, while we can still securely generate an optimized, zero-setup interactive sandbox build specifically for public demo purposes.

## 5. Consequences & Trade-offs
* **Positive Impact:** Secure compliance for standard production, zero-setup developer sandbox, easier product demonstrations.
* **Negative Impact / Technical Debt:** Requires keeping mock roles and fallback identities in sync with seeded Keycloak configurations.
* **Mitigation Strategy:** Automated smoke and unit tests cover both standard OIDC lookup fallbacks and demo mode login scenarios.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `apps/web/package.json`, `apps/web/src/main.js`, `apps/web/src/stores/auth.js`, `apps/web/tests/auth.test.js`, `package.json`
* **Verification Plan:** Verify compile parameters with `pnpm build:demo` and run `pnpm -r test` to ensure all 299+ unit and integration tests successfully validate the OIDC bypass logic.
