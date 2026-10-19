# Subject portal

Participant-facing eCOA/ePRO and eConsent web app (Vue 3, Vite) with an offline sync queue and a service worker. It calls the API gateway at `http://localhost:8000`.

## Layout

- `src/index.js`: app state, API dispatch, Keycloak sign-in and the offline flow.
- `src/App.vue`: UI; `src/style.css`: styles.
- `src/sync-queue.js`: IndexedDB-backed offline submission queue (the coverage-gated module).
- `src/mocks/`: MSW handlers, started in dev and `demo` builds or when `VITE_ENABLE_MSW=true`.
- `public/`: `manifest.json`, `sw.js` (service worker), `mockServiceWorker.js` (MSW).
- `tests/`: Vitest unit tests; `tests/e2e/`: Playwright specs.

## Backend and authentication

- Requests go to `http://localhost:8000/api/v1/...`; paths without an `api/v1/` prefix are sent to `/api/v1/interop/...`. The base URL is hard-coded in `src/index.js`.
- When Keycloak sign-in succeeds (`VITE_KEYCLOAK_URL`, `VITE_KEYCLOAK_REALM`, `VITE_KEYCLOAK_CLIENT_ID`; defaults `http://localhost:8080/`, `cadence`, `cadence-web`), the access token is sent as `Authorization: Bearer <token>`. Without a token the gateway rejects every proxied request (see [apps/gateway](../gateway/README.md)).
- The interop service currently maps only its status endpoint (see [apps/interop](../interop/README.md)), so the portal's interop calls have no backend implementation yet.

## Scripts

Run from the repository root with pnpm:

| Command                                 | Purpose                                                        |
| --------------------------------------- | -------------------------------------------------------------- |
| `pnpm --filter subject-portal dev`      | Vite dev server on `http://localhost:5174/subject-portal/`.    |
| `pnpm --filter subject-portal build`    | Production build. Build `ui` first (`pnpm --filter ui build`). |
| `pnpm --filter subject-portal test`     | Vitest with coverage (80% thresholds on `src/sync-queue.js`).  |
| `pnpm --filter subject-portal test:e2e` | Playwright.                                                    |
| `pnpm --filter subject-portal lint`     | ESLint.                                                        |

`docker/docker-compose.yml` also runs the dev server as the `subject-portal` service on port 5174.

## Dependencies

- Runtime: `vue` and the workspace packages [ui](../../packages/ui/README.md) and [usdm-schemas](../../packages/usdm-schemas/README.md); `keycloak-js` is loaded on demand.
- Development: Vite, Vitest, Playwright with `@axe-core/playwright`, MSW, `fake-indexeddb`, `jsdom`.
