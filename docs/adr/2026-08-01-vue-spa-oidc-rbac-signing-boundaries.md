# ADR-052: Vue SPA, OIDC, RBAC, and Signing Boundaries

* **Status:** Accepted
* **Date:** 2026-08-01
* **Authors:** @jules
* **Deciders:** @fderuiter, @jules

---

## 1. Context & Problem Statement
The current frontend architecture of the Cadence Clinical platform utilizes a static site demo under `apps/web` which lacks full-featured dynamic Single Page Application (SPA) capabilities. To transition the platform's client-side user experience into a robust, high-performance, and secure clinical trial management system, we need to replace this static demo with a modern Vue 3 SPA.

Furthermore, we must establish and document explicit security, authentication, and cryptographic boundaries. The platform must implement secure OpenID Connect (OIDC) authentication with Keycloak, define robust role-based access control (RBAC) route guards within the SPA, and outline clear rules regarding cryptographic operations. Specifically, trusted identity headers and request signing must remain gateway/server-side; the browser code must not contain shared signing secrets or perform core server-side digital signature transformations.

This decision implements requirements under Trace-8.

## 2. Decision Drivers & Constraints
* **Driver 1 (User Experience & Reactivity):** Clinical users (e.g., Clinical Research Associates, Investigators, and Monitors) require highly interactive, reactive, and localized interfaces that support fast route transitions without page reloads.
* **Driver 2 (Compliance & Security Boundaries):** In accordance with FDA 21 CFR Part 11 and GxP standards, the client application must prevent unauthorized users from viewing or interacting with protected routes. However, client-side route guards are purely for user experience; actual security enforcement must be done at the API gateway and backend services.
* **Driver 3 (Cryptographic Key Protection):** Shared secrets, private keys, and high-trust cryptographic operations must NEVER reside in or be executed by browser code to prevent credential extraction and unauthorized token generation.
* **Driver 4 (Deployment and Routing Compatibility):** The SPA must be hostable on standard static hosting environments, specifically GitHub Pages, requiring a solution for single-entrypoint routing (e.g., Vue Router HTML5 history fallback handling).

## 3. Options Considered

### Option 1: React with Next.js (Server-Side Rendered or Static Export)
Use React and Next.js as the frontend framework, configured either as a server-side rendered (SSR) application or statically exported.
* **Pros:**
  * ✅ Extremely large ecosystem and robust server-side data fetching capabilities.
  * ✅ Solid OIDC integration support via NextAuth or similar libraries.
* **Cons:**
  * ❌ Higher complexity and bundle size compared to lightweight options.
  * ❌ Static HTML export removes key dynamic routing benefits, whereas full SSR introduces complex backend hosting requirements that are incompatible with simple GitHub Pages hosting.

### Option 2: Vue 3 SPA with Vue Router, Pinia, and keycloak-js (Selected)
Replace `apps/web` with a Vue 3 SPA leveraging Vue Router for client-side routing, Pinia for lightweight state management, and the official `keycloak-js` adapter for OIDC authentication.
* **Pros:**
  * ✅ Outstanding performance, excellent developer velocity, and extremely low bundle size.
  * ✅ Vue Router provides simple, declarative metadata support perfect for role-based route guards.
  * ✅ Pinia offers high-performance, type-safe global state store integration with Vue 3's composition API.
  * ✅ `keycloak-js` is the standard OIDC integration tool for Keycloak, enabling standard Authorization Code Flow with Proof Key for Code Exchange (PKCE) natively in the browser.
  * ✅ Highly compatible with static builds deployed to GitHub Pages.
* **Cons:**
  * ❌ Client-side routing on GitHub Pages requires standard workaround scripts (like `spa-github-pages` or custom 404 fallbacks) to prevent 404 errors on direct sub-page navigation.

## 4. Decision Outcome

* **Chosen Option:** Option 2
* **Justification:** Vue 3, combined with Pinia and Vue Router, delivers the optimal blend of simplicity, speed, and compatibility with the existing pnpm frontend workspace. It cleanly supports our OIDC auth and client-side RBAC needs while maintaining compatibility with static builds on GitHub Pages.

---

### Architectural Specifications & Boundaries

#### A. OIDC Authentication Protocol
1. **Flow:** The client uses the Authorization Code Flow with PKCE (RFC 7636). Implicit Flow and standard Resource Owner Password Credentials flow are strictly prohibited due to security vulnerabilities.
2. **Keycloak Integration:** The frontend loads and initializes `keycloak-js`. Configured with the `cadence` realm, client ID, and standard OIDC discovery endpoints.
3. **Session Management:** Pinia stores the authenticated tokens (ID, Access, and Refresh tokens). Token refresh is automated via a background timer using `keycloak.updateToken()`.

#### B. Client-Side Role-Based Access Control (RBAC)
1. **Route Metadata:** Vue Router configuration defines routes with custom `meta` fields specifying the required roles (e.g., `meta: { requiresRole: ['Monitor', 'Sponsor Admin'] }`).
2. **Navigation Guards:** A global beforeEach guard (`router.beforeEach`) intercepts navigation:
   - Verifies if the user is authenticated.
   - Decodes the JWT payload (extracted by `keycloak-js` under `tokenParsed.realm_access.roles`) to check if the user possesses the necessary role.
   - Redirects unauthorized users to an Access Denied page or forces Keycloak login.
3. **Defense-in-Depth Principle:** SPA-level route guards are exclusively for visual structure and UX. All mutations, transitions, and reads must be independently verified by the API Gateway and backend services using verified JWT signatures.

#### C. Static Deployment & GitHub Pages Routing
1. **Base Path Configuration:** The build pipeline is configured with a base path prefix (e.g., `/cadence-clinical/`) to ensure asset resolution works seamlessly on GitHub Pages.
2. **Routing Workaround:** Since GitHub Pages does not support native rewrite rules for HTML5 History Mode routing, a custom `404.html` is packaged in the production build to redirect the browser to the index page while preserving the original route path as a query parameter, which Vue Router parses and resolves on initialization.

#### D. Cryptographic and Signing Boundaries
1. **No Shared Signing Secrets:** Private keys, symmetric HMAC secrets, or other signing credentials must **never** be hardcoded, cached, or compiled into the client-side bundle.
2. **Server-Side Signature Validation:** Digital signatures (e.g., 21 CFR Part 11 electronic signatures or Merkle ledger sealing) are generated and validated on the server or gateway side.
3. **Browser Signing Restrictions:** Client code must never perform core asymmetric or symmetric signing operations directly. Instead, the browser acts as a conduit: collecting user credentials or digital certificate blocks from a secure hardware token/identity provider and forwarding them to the Gateway via HTTPS, where the signature block is cryptographically bound and validated.

## 5. Consequences & Trade-offs

* **Positive Impact:**
  * ✅ Modern, reactive, and standard-compliant frontend architecture.
  * ✅ Bulletproof audit trails backed by OIDC tokens and client-side RBAC.
  * ✅ Clear isolation of cryptographic responsibilities preventing secret leakage.
* **Negative Impact / Technical Debt:**
  * ❌ Client-side route matching requires custom 404 rewrite handling on GitHub Pages.
  * ❌ Token expiration and silent refresh timers must be carefully managed to avoid session dropouts during clinical data entry.

## 6. Implementation & Verification

* **Affected Repositories / Services:** `apps/web/`
* **Verification Plan:**
  1. Verify build pipeline correctness by compiling static assets with `pnpm --filter web build`.
  2. Verify routing configuration by loading the entry point and validating OIDC token parsing.
  3. Continuous Integration tests will execute automated Playwright or Vitest suites to verify that route guards block unauthenticated/unauthorized navigation.
