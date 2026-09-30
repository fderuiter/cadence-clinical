# ADR-2493: Keycloak-Only Web Sign-In

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter, Claude Code
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001

---

## 1. Context & Problem Statement

Since [ADR-2256](2026-09-29-fail-closed-gateway-identity-and-signed-caller-authentication.md) the gateway requires a Keycloak bearer token on every API route. #4856 made the web app's demo mode opt-in ([ADR-2329](2026-08-04-compile-time-vite-build-mode-for-interactive-public-demo.md), demo builds only), but a regular build could still act as a user Keycloak never issued:

- The login page's offline PIN unlock set `isAuthenticated = true` from a PIN-wrapped session in IndexedDB, with no token and no identity provider.
- `useAuthStore` persisted access, ID and refresh tokens to `sessionStorage` and restored `isAuthenticated`, the user and roles from it on the next load.
- The header persona picker replaced the user's roles with a preset, so any signed-in user could act as Super Admin in the UI.
- `soaClient.ts` signed requests in the browser with a hard-coded gateway secret, and `RactMatrixViewer.vue` sent spoofed `X-User-*` headers.
- Tokens were attached ad hoc, and some direct `fetch("/api/...")` calls sent none.
- The `cadence-web` Keycloak client allowed every web origin (`*`) and did not require PKCE.

Under 21 CFR Part 11 the UI must not present or act as an identity or role the user was not issued.

## 2. Decision Drivers & Constraints

- **Part 11 attribution (PRD-SYS-001):** the identity the UI shows and acts as is the authenticated Keycloak user.
- **Fail closed:** a missing or unreachable identity provider means nobody is signed in, never a fallback user.
- **No secrets in the browser:** the bundle holds no gateway or signing secret; the gateway signs forwarded identity (ADR-2256).
- **OAuth 2.0 for browser apps (RFC 9700):** public client, authorization code flow with PKCE, exact redirect URIs, tokens kept out of web storage.
- **Keep the public demo:** ADR-2329's compile-time demo build stays, isolated from every other build.

## 3. Options Considered

### Option 1: Also retire the demo build

- **Pros:** one authentication path in the source tree.
- **Cons:** removes the zero-setup public sandbox that #4856 deliberately kept opt-in.

### Option 2: Keycloak-only sign-in outside the demo build (Selected)

- **Overview:** every non-demo build signs in only through Keycloak (`keycloak-js`, `check-sso`, PKCE S256). A demo build keeps its offline demo identity and mock gateway, decided at compile time.
- **Pros:** one authentication path in every real build; the demo stays available.
- **Cons:** running the real UI needs Keycloak (the Docker realm) and the gateway.

## 4. Decision Outcome

- **Chosen Option:** Option 2.
- **Sign-in:** `src/auth/bootstrap.js` creates the Keycloak client from `VITE_KEYCLOAK_URL`, `VITE_KEYCLOAK_REALM` and `VITE_KEYCLOAK_CLIENT_ID` and runs `init({ onLoad: "check-sso", pkceMethod: "S256" })` before the router starts. Production builds have no Keycloak defaults: a missing value is reported on the sign-in page. If Keycloak is unreachable, nobody is signed in and the sign-in page shows the error.
- **Routing:** routes with `requiresAuth` send a signed-out user to `/login`, which has a single "Sign in with Keycloak" button and only accepts in-app redirect targets. The offline PIN unlock and PIN setup are removed.
- **Session:** tokens stay in memory (`keycloak-js` and `useAuthStore`); nothing is persisted and the legacy `cadence_auth` storage key is purged. Tokens are refreshed on `onTokenExpired`, before navigation to protected routes and before each API call; a failed refresh ends the session. Logout ends the Keycloak SSO session.
- **Token attachment:** `src/api/authorization.js` is the one place that adds `Authorization: Bearer`, for `apiClient.js`, the openapi-fetch client and the upload client, plus a scoped `fetch` wrapper for components that call `fetch("/api/...")`. Tokens go only to same-origin `/api/` paths and the configured API base URL.
- **Roles and personas:** roles are `realm_access.roles`. A persona only picks the workspace view, help and onboarding content, is limited to the personas the token's roles entitle, and never changes roles. Role implication is one-way (a Sponsor Admin holds every UI role; nothing implies Sponsor Admin).
- **Demo build:** only when `isDemoBuild()` is true (`--mode demo` or `VITE_DEMO_MODE=true`) does `main.js` skip Keycloak and call `useAuthStore().startDemoSession()`, which throws in any other build.
- **Removed:** offline PIN sign-in, token persistence, role-granting personas, client-side gateway signing in `soaClient.ts` and `X-User-*` headers in `RactMatrixViewer.vue`.
- **Keycloak client (`docker/cadence-realm.json`):** `cadence-web` is public with the standard flow only, `pkce.code.challenge.method: S256`, `redirectUris: ["http://localhost:3000/cadence-clinical/*"]`, `webOrigins: ["+"]` (was `*`) and `post.logout.redirect.uris: "+"`. Development-only users for the Playwright personas are seeded.

## 5. Consequences & Trade-offs

- **Positive Impact:** a real build shows and acts as the signed-in Keycloak user only. No token is written to web storage and no secret ships in the bundle.
- **Negative Impact / Technical Debt:**
  - The offline queue escrow (`src/utils/offlineAuth.ts`, `syncEngine.ts`) no longer has a sign-in entry point. No view queues offline changes today; offline capture needs a Keycloak-bound design (offline tokens) before it is used.
  - The subject portal (`apps/subject-portal`) is unchanged and still has its own sign-in handling.
- **Mitigation Strategy:** deployments configure `VITE_KEYCLOAK_*` and add their redirect URI to the client.

## 6. Implementation & Verification

- **Affected:** `apps/web` (`src/auth/`, `src/api/`, `src/stores/auth.js`, `src/stores/clinical.ts`, `src/router/index.js`, `src/main.js`, `src/views/LoginView.vue`, `src/components/AppShell.vue`, tests), `docker/cadence-realm.json`, `.env.example`, `docs/ENVIRONMENT_VARIABLES.md`.
- **Verification:**
  - `apps/web/tests/keycloak_signin.test.js`: config fails closed in production builds; bootstrap uses `check-sso` + PKCE S256; an unreachable Keycloak signs nobody in; a failed refresh ends the session; bearer tokens are attached to gateway requests only.
  - `apps/web/tests/auth.test.js`: no fallback identity outside a demo build; legacy storage is purged and tokens are never persisted; sign-in, sign-out and refresh delegate to Keycloak; personas cannot grant roles; demo sign-in throws outside a demo build.
  - `apps/web/tests/router_rbac.test.js`: signed-out users are sent to `/login`; authorization uses token roles.
  - `tests/e2e/auth.setup.ts` signs in through the Keycloak login form.
