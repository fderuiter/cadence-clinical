# Web client

Staff-facing single-page application (Vue 3, Pinia, Vue Router, Vite). It talks only to the API gateway, which forwards requests to the backend services.

## Layout

- `src/main.js`: bootstraps Vue and Pinia, restores the Keycloak session (`src/auth/bootstrap.js`) before starting the router, and, in a demo build only, signs in the offline demo identity and starts the MSW mock worker.
- `src/auth/`: the Keycloak client configuration (`keycloak.js`) and session bootstrap (`bootstrap.js`).
- `src/api/`: the `apiClient.js` fetch wrapper and per-service clients. `executionApi.ts` is typed from the .NET Execution spec through `src/api/generated/execution.ts`; the others (`designer.ts`, `execution.ts`, `etmf.ts`, `interop.ts`, `econsent.ts`, `fileshare.ts`, and more) are Python-era clients.
- `scripts/generate-api-types.js`: generates `src/api/generated/<service>.ts` from `docs/openapi/<service>_openapi.json` (`pnpm --filter web generate:api`); the `api-clients` quality gate and `tests/api/generated_types.test.js` fail when they drift from the specs.
- `src/stores/`: Pinia stores; `auth.js` holds the Keycloak session and role normalisation.
- `src/views/`, `src/components/`, `src/features/`, `src/composables/`, `src/router/`: UI.
- `src/canvas/` and `src/components/canvas/`: the Clinical Canvas (`StudyCanvasView.vue`, route `/studies/:studyCode`): lifecycle stages and sections, the browser mirror of the kernel NodeRef (ADR-2481), the palette, pulse, participants and inspector. `src/stores/studyCanvas.ts` holds what it has read from Execution.
- `src/mocks/`: MSW handlers for offline and demo use.
- `src/types/generate-types.js`: generates API types before build, test and typecheck.
- `tests/`: Vitest unit tests; `tests/e2e/`: Playwright specs.

## Authentication

Sign-in is Keycloak only ([ADR-2493](../../docs/adr/2026-09-30-keycloak-only-web-sign-in.md)).

- The client is a public OIDC client: authorization code flow with PKCE S256, restoring an existing session with `check-sso` before the router starts. Development defaults: `VITE_KEYCLOAK_URL=http://localhost:8080/`, `VITE_KEYCLOAK_REALM=cadence`, `VITE_KEYCLOAK_CLIENT_ID=cadence-web`. Production builds have no defaults; a missing value is shown on the sign-in page. The realm is in `docker/cadence-realm.json`.
- Tokens stay in memory and are never written to web storage. They are refreshed before expiry, before navigating to a protected route and before each API call; a failed refresh ends the session and returns to `/login`. Signing out ends the Keycloak session.
- `src/api/authorization.js` is the only place that adds `Authorization: Bearer <token>`: for `apiClient.js`, the openapi-fetch client, the upload client and direct `fetch("/api/...")` calls. It sends the token only to same-origin `/api/` paths and `VITE_API_BASE_URL` (default `http://localhost:8000`). `apiClient.js` also sends `X-Change-Reason` on mutations when the caller supplies one. Any code that sets `X-Change-Reason` encodes the value with `encodeChangeReason` (`src/api/changeReason.ts`): fetch rejects header characters above U+00FF, so a reason with an em dash or in a non-Latin script travels as RFC 8187 `UTF-8''` percent-encoded UTF-8 ([ADR-2495](../../docs/adr/2026-09-30-reason-for-change-header-encoding.md)).
- Identity and roles come from the token (`preferred_username`, `sub`, `realm_access.roles`). The persona picker only changes the workspace view, lists only the personas the token's roles entitle, and never changes roles.
- There is no offline sign-in. When Keycloak is unreachable nobody is signed in and the sign-in page reports the problem. The gateway requires a valid bearer token on every proxied route (see [apps/gateway](../gateway/README.md)).

## Demo mode and API failures

Demo mode is opt-in at build time: `pnpm --filter web dev --mode demo`, `pnpm --filter web build:demo`, or `VITE_DEMO_MODE=true` (`src/utils/demoMode.js`). Only a demo build signs in with the offline demo identity, starts the MSW mock API and loads sample data. Every other build talks to the gateway. A failed API call shows an error alert (`useToastStore().apiError(title, err)` from `src/stores/toasts.js`) and leaves the screen as it was; never substitute sample data or report success when a call fails. Tests opt into a demo build with `vi.stubEnv("VITE_DEMO_MODE", "true")`.

Some regulated actions have no API behind them yet: enrolment, re-consent and queries from the eCRF (Execution serves queries, but the eCRF's belong to its sample form until real form definitions exist, #4828), SDV, query transitions in the rules screen, ticket workflow and signatures, safety and CEC sign-offs, eConsent signing and ICF publishing, exports, protocol commit, medical coding suggestions, and quality actions. Each one starts with `allowUnbackedAction("<what it does>")` from `src/utils/unbackedActions.js`. Outside a demo build that refuses the action, tells the user nothing was saved, and returns `false` so the caller changes nothing (#4827). Remove the call when the action moves onto a real endpoint.

## Screen availability

Most screens were written against the Python services and call endpoints no .NET service serves. Only the eCRF and CTMS screens have a backend behind them (Execution). The eCRF lists the subjects of the chosen study from Execution; the study comes from `?studyId=` or the study field above the form. CTMS outside a demo build is site activation only (eTMF status and activation, `src/components/clinical/SiteActivationPanel.vue`); its other tabs have no backend and run only in a demo build. The Clinical Canvas (`/studies/:studyCode`, opened from **Studies** in the header) is the study-centric workspace these screens move into ([CLINICAL_CANVAS.md](../../docs/ux/CLINICAL_CANVAS.md)). It renders without the product sidebar, shows only what a service serves (participants and their counts, site activation) and leaves every other stage and verb empty or disabled with the issue it waits on. Every other screen is hidden from the sidebar and command palette, and its route shows a "not available yet" page, until its module serves the endpoints it needs. `AVAILABLE_SCREENS` in `src/router/availability.js` lists the visible screens. Every visible screen must have a view test: `tests/available_screen_tests.test.js` fails when a route in `AVAILABLE_SCREENS` has no test that imports its view. Hidden screens get tests when they move onto a real API, not before, because most will be rewritten (#4529). [`docs/web/API_MIGRATION.md`](../../docs/web/API_MIGRATION.md) has the screen-by-screen inventory and the steps for moving a screen over, and epic #4833 has the plan.

Apart from `executionApi.ts`, the clients in `src/api/` are still typed against the Python-era specs in `docs/openapi/legacy/`. They are replaced as each screen moves over.

## Scripts

Run from the repository root with pnpm (the workspace is defined in `pnpm-workspace.yaml`):

| Command                                | Purpose                                                                                                     |
| -------------------------------------- | ----------------------------------------------------------------------------------------------------------- |
| `pnpm --filter web dev`                | Vite dev server on `http://localhost:3000/cadence-clinical/`; `/api` is proxied to `http://localhost:8000`. |
| `pnpm --filter web build`              | Production build (also builds `usdm-schemas` and `ui` first, then the docs site).                           |
| `pnpm --filter web build:demo`         | Build in `demo` mode (MSW enabled).                                                                         |
| `pnpm --filter web test`               | Vitest with coverage.                                                                                       |
| `pnpm --filter web test:e2e`           | Playwright; starts the web and subject-portal dev servers.                                                  |
| `pnpm --filter web lint` / `typecheck` | ESLint / `tsc --noEmit`.                                                                                    |
| `pnpm --filter web generate:api`       | Regenerates `src/api/generated/` from the .NET services' specs in `docs/openapi/`.                          |

## Dependencies

- Runtime: `vue`, `vue-router`, `pinia`, `keycloak-js`, `openapi-fetch`, `hls.js`, `vuedraggable`, and the workspace packages [ui](../../packages/ui/README.md) and [usdm-schemas](../../packages/usdm-schemas/README.md).
- Development: Vite, Vitest, Playwright with `@axe-core/playwright`, MSW, `openapi-typescript`, TypeScript.

## Running against the backend

Start Keycloak and the services (for example `docker compose -f docker/docker-compose.yml up`, or the gateway and services with `dotnet run`), then `pnpm --filter web dev` and sign in with a user from the `cadence` realm.
