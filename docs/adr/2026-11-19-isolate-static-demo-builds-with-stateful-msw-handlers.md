# ADR-2546: Isolate Static Demo Builds with Stateful MSW Handlers

- **Status:** Accepted
- **Date:** 2026-11-19
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001 (Operational boundary isolation and production vs. demo runtime separation)

---

## 1. Context & Problem Statement

Cadence Clinical ships opt-in static demo builds (`--mode demo` / `VITE_DEMO_MODE=true`) for both the staff web workspace (`apps/web`, served under `/cadence-clinical/`) and the participant portal (`apps/subject-portal`, served under `/subject-portal/`) so evaluators can explore clinical workflows on static hosts without running the .NET gateway, PostgreSQL, or Keycloak.

Previously, demo builds had four isolation gaps:

1. **Subpath worker registration & startup races:** MSW service worker URLs were not resolved relative to `import.meta.env.BASE_URL` and startup was not awaited before initial route navigation and view mounting, allowing early API queries to escape interception.
2. **Off-origin localhost leaks & passthrough fallbacks:** Unset `VITE_API_BASE_URL` defaulted to `http://localhost:8000`, participant portal `login()` still attempted Keycloak initialization against `http://localhost:8080/`, and `handlers.js` defaulted to `passthrough()` for unmocked endpoints.
3. **Dual service worker scope collision in `subject-portal`:** Registering both `/subject-portal/sw.js` and `/subject-portal/mockServiceWorker.js` under `/subject-portal/` caused registration eviction and risked deleting MSW `msw:` Cache Storage client-tracking buckets.
4. **Stateless mocks & locked screens:** Mutations did not persist across subsequent session reads, and demo-supported workspace screens outside `AVAILABLE_SCREENS` redirected to `/unavailable`.

## 2. Decision Drivers & Constraints

- **Complete network isolation in demo builds:** Zero requests may target `http://localhost:8000` or `http://localhost:8080`, and unmocked API routes must fail closed rather than passing through to the network.
- **Production purity (ADR-2329, ADR-2493):** Standard, test, and production builds must preserve strict Keycloak authentication, gateway error reporting, `AVAILABLE_SCREENS` restrictions, and Rollup tree-shaking of mock workers and synthetic fixtures.
- **OpenAPI & Kernel fidelity (ADR-2540, ADR-2620, ADR-2622):** Synthetic demo responses and mutations must conform to `generated/kernel.ts` and `generated/execution.ts` schemas using deterministic, fictional de-identified fixtures only.

## 3. Options Considered

### Option 1: Base-Aware Awaited MSW Startup, Fail-Closed Stateful Handlers & Single-Scope SW Coordination (Selected)

- **Overview:**
  - Resolve worker URLs and scopes from `import.meta.env.BASE_URL` and `await startMswWorker()` before mounting views or starting initial route navigation in both apps.
  - Default `getApiBaseUrl()` / `getBaseUrl()` to `globalThis.location.origin` in demo builds when `VITE_API_BASE_URL` is unset, and short-circuit `login()` in `apps/subject-portal` to activate `subject_001` locally in demo builds.
  - Implement stateful in-memory stores (`demoStore.js`, `demoHandlers.js`) serving OpenAPI-compliant responses and persisting mutations across reads within the browser session, backed by a fail-closed boundary (`501 DEMO_UNMOCKED_ENDPOINT`, `503 DEMO_OFF_ORIGIN_BLOCKED`).
  - Coordinate offline shell caching and MSW interception under `/subject-portal/sw.js?msw=1` via `importScripts("./mockServiceWorker.js")`, `event.stopImmediatePropagation()` on shell assets, and preservation of `msw:` Cache Storage keys.
  - Unlock `DEMO_AVAILABLE_SCREENS` in `apps/web/src/router/availability.js` only when `isDemoBuild()` is true, and strip `dist/mockServiceWorker.js` in production builds.
- **Pros:**
  - Eliminates startup race conditions, subpath 404s, service worker scope collisions, and localhost network leaks on static hosts.
  - Preserves 100% Rollup dead-code elimination and `AVAILABLE_SCREENS` / Keycloak enforcement in production builds.
- **Cons:**
  - Requires keeping synthetic demo handler schemas aligned with Kernel and Execution OpenAPI specs.

### Option 2: Component-Level Fallback Fixtures on Network Catch Blocks

- **Overview:** Catch failed `fetch` calls inside individual stores/views in demo mode and populate fallback sample arrays.
- **Pros:**
  - No service worker coordination needed.
- **Cons:**
  - Still emits failed browser network requests to `localhost:8000` / `localhost:8080`, scatters demo conditionals across production clinical stores, and breaks cross-screen state consistency.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Centralizing static demo simulation behind awaited, base-aware, fail-closed MSW handlers keeps clinical UI components and stores clean while guaranteeing zero external network leakage and realistic stateful workflows on static hosts.

## 5. Consequences & Trade-offs

- **Positive Impact:** Compiled demo bundles (`build:demo`) run self-contained under `/cadence-clinical/` and `/subject-portal/` with stateful mutations across Kernel, Execution, CTMS/SSU, MDR, and Participant Portal journeys, while production bundles (`build`) omit `mockServiceWorker.js` and tree-shake all demo modules.
- **Negative Impact / Technical Debt:** Newly added demo-visible endpoints must register a synthetic handler in `demoHandlers.js` or will intentionally surface `501 DEMO_UNMOCKED_ENDPOINT`.
- **Mitigation Strategy:** Deterministic `501 DEMO_UNMOCKED_ENDPOINT` responses make missing demo handlers immediately visible during testing rather than silently hanging on unreachable ports.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/web`, `apps/subject-portal`, `packages/ui`.
- **Verification Plan:** Unit suites (`apps/web/tests/demo_isolation_stateful_msw.spec.js`, `apps/subject-portal/tests/demo_isolation_portal.test.js`, `apps/subject-portal/tests/sw.test.js`), production bundle tree-shaking checks, and headless Chromium verification of compiled demo bundles under `/cadence-clinical/` and `/subject-portal/`.
