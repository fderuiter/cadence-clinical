# Web client

Staff-facing single-page application (Vue 3, Pinia, Vue Router, Vite). It talks only to the API gateway, which forwards requests to the backend services.

## Layout

- `src/main.js`: bootstraps Vue, Pinia and the router, initialises Keycloak (`keycloak-js`), and, in a demo build only, seeds the offline demo identity and the MSW mock worker.
- `src/api/`: the `apiClient.js` fetch wrapper and per-service clients. `executionApi.ts` is typed from the .NET Execution spec through `src/api/generated/execution.ts`; the others (`designer.ts`, `execution.ts`, `etmf.ts`, `interop.ts`, `econsent.ts`, `fileshare.ts`, and more) are Python-era clients.
- `scripts/generate-api-types.js`: generates `src/api/generated/<service>.ts` from `docs/openapi/<service>_openapi.json` (`pnpm --filter web generate:api`); `tests/api/generated_types.test.js` fails when they drift.
- `src/stores/`: Pinia stores; `auth.js` holds the Keycloak session and role normalisation.
- `src/views/`, `src/components/`, `src/features/`, `src/composables/`, `src/router/`: UI.
- `src/mocks/`: MSW handlers for offline and demo use.
- `src/types/generate-types.js`: generates API types before build, test and typecheck.
- `tests/`: Vitest unit tests; `tests/e2e/`: Playwright specs.

## Authentication

- The client signs in through Keycloak (Authorization Code with PKCE). Defaults: `VITE_KEYCLOAK_URL=http://localhost:8080/`, `VITE_KEYCLOAK_REALM=cadence`, `VITE_KEYCLOAK_CLIENT_ID=cadence-web`. The realm is in `docker/cadence-realm.json`.
- `apiClient.js` sends the Keycloak access token as `Authorization: Bearer <token>` to `VITE_API_BASE_URL` (default `http://localhost:8000`), and `X-Change-Reason` on mutations when the caller supplies one.
- The gateway requires a valid bearer token on every proxied route (see [apps/gateway](../gateway/README.md)). When Keycloak is unreachable the app opens the login page and sign-in reports the problem; it does not fall back to a demo identity.

## Demo mode and API failures

Demo mode is opt-in at build time: `pnpm --filter web dev --mode demo`, `pnpm --filter web build:demo`, or `VITE_DEMO_MODE=true` (`src/utils/demoMode.js`). Only a demo build signs in with the offline demo identity, starts the MSW mock API and loads sample data. Every other build talks to the gateway. A failed API call shows an error alert (`useToastStore().apiError(title, err)` from `src/stores/toasts.js`) and leaves the screen as it was; never substitute sample data or report success when a call fails. Tests opt into a demo build with `vi.stubEnv("VITE_DEMO_MODE", "true")`.

Some regulated actions have no API at all yet: enrolment and re-consent from the eCRF, SDV, query transitions in the rules screen, ticket workflow and signatures, safety and CEC sign-offs, eConsent signing and ICF publishing, exports, protocol commit, medical coding suggestions, and quality actions. Each one starts with `allowUnbackedAction("<what it does>")` from `src/utils/unbackedActions.js`. Outside a demo build that refuses the action, tells the user nothing was saved, and returns `false` so the caller changes nothing (#4827). Remove the call when the action moves onto a real endpoint.

## Screen availability

Most screens were written against the Python services and call endpoints no .NET service serves. Only the eCRF and CTMS screens have a backend behind them (Execution). The eCRF lists the subjects of the chosen study from Execution; the study comes from `?studyId=` or the study field above the form. Every other screen is hidden from the sidebar and command palette, and its route shows a "not available yet" page, until its module serves the endpoints it needs. `AVAILABLE_SCREENS` in `src/router/availability.js` lists the visible screens. Every visible screen must have a view test: `tests/available_screen_tests.test.js` fails when a route in `AVAILABLE_SCREENS` has no test that imports its view. Hidden screens get tests when they move onto a real API, not before, because most will be rewritten (#4529). [`docs/web/API_MIGRATION.md`](../../docs/web/API_MIGRATION.md) has the screen-by-screen inventory and the steps for moving a screen over, and epic #4833 has the plan.

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
