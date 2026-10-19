# Subject portal

Participant-facing eCOA/ePRO and eConsent web app (Vue 3, Vite) with an offline sync queue and a service worker. It calls the API gateway at `http://localhost:8000`.

## Layout

| Path                    | Contents                                                                                                                                                                                                                      |
| ----------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `src/index.js`          | Entry point loaded by `index.html`: runs `initializeApp` on `DOMContentLoaded` and re-exports the portal's public API (`state`, `showView`, `syncOfflineQueue`, ...), which the tests import from here.                       |
| `src/App.vue`           | The shell: header, sync drawer, tab bar, participant card, banners, every view, the modals and the audit ledger footer.                                                                                                       |
| `src/views/`            | One component per view (`TasksView`, `QuestionnaireView`, `ComplianceView`, `InboxView`, `ConsentView`, `WalletView`, `LaySummariesView`); each is the view's `<section class="portal-view">`.                                |
| `src/components/`       | Reusable pieces: `PortalHeader`, `SyncDrawer`, `TabBar`, `ParticipantCard`, `ReconsentBanner`, `SyncSuspendedBanner`, `SignatureModal`, `ReconsentModal`, `PinSetupModal`, `PinUnlockModal`, `LedgerFooter`, `focus-trap.js`. |
| `src/router/`           | `routes.js` (one route per view, which also defines the tabs) and `index.js` (the router and its URL and view synchronisation).                                                                                               |
| `src/stores/`           | Pinia stores (`session`, `ui`, `tasks`, `compliance`, `inbox`, `consent`, `sync`, `wallet`, `audit`), the shared Pinia instance and `state.js`, the `state` facade over them.                                                 |
| `src/features/`         | One module per feature: `tasks`, `questionnaire`, `signature`, `compliance`, `inbox`, `consent`, `reconsent`, `sync`, `sync-status`, `pin`, `auth` (cached identity, Keycloak, logout), `wallet`, `audit`, `navigation`.      |
| `src/api/client.js`     | `dispatchApi` and the typed fetch helpers.                                                                                                                                                                                    |
| `src/bootstrap/`        | `initialize.js` (`initializeApp`), `mount.js` (mounts the shell with Pinia and the router), `controls.js` (button bindings and the tab list's ARIA and keyboard handling).                                                    |
| `src/widgets/`          | HTML-string form widgets (`clinical-fields.js`), inline field errors and the toast.                                                                                                                                           |
| `src/data/mock-data.js` | Sandbox data used without an authenticated gateway session.                                                                                                                                                                   |
| `src/sync-queue.js`     | IndexedDB-backed offline submission queue (the coverage-gated module).                                                                                                                                                        |
| `src/mocks/`            | MSW handlers, started in dev and `demo` builds or when `VITE_ENABLE_MSW=true`.                                                                                                                                                |
| `src/style.css`         | Styles.                                                                                                                                                                                                                       |
| `public/`               | `manifest.json`, `sw.js` (service worker), `mockServiceWorker.js` (MSW).                                                                                                                                                      |
| `tests/`, `tests/e2e/`  | Vitest unit tests; Playwright specs.                                                                                                                                                                                          |

### State, router and views

- **State.** The Pinia stores hold the portal's state. The feature modules (and the tests) use `state` from `src/stores/state.js`: one object whose top-level keys (`state.session`, `state.assignments`, `state.currentView`, ...) read and write the store that declares them, so it behaves like the single reactive object the portal used to keep. Components use the stores directly.
- **Views stay mounted.** The feature modules render tasks, compliance, the inbox, consent and the sync queue into elements by id (`tasks-list-container`, `compliance-history-tbody`, ...), so all views are always in the DOM; `state.currentView` decides which one has the `active` class and which tab is selected.
- **Router.** `vue-router` with `createWebHistory("/subject-portal/")` gives each view a URL: `/` (tasks), `/questionnaire`, `/compliance`, `/inbox`, `/consent`, `/wallet`, `/lay-summaries`. `showView(viewId)` (or any write to `state.currentView`) pushes the view's route, and a URL change (back/forward, a deep link) activates its view. A deep link to `/questionnaire` without a questionnaire in progress opens the tasks. Under Vitest the router uses memory history, and mounting keeps the current view. A static host must serve `index.html` for every path under `/subject-portal/` (Vite's dev and preview servers do).

## Backend and authentication

- Requests go to `http://localhost:8000/api/v1/...`; paths without an `api/v1/` prefix are sent to `/api/v1/interop/...`. The base URL is hard-coded in `src/api/client.js`.
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

- Runtime: `vue`, `vue-router`, `pinia` and the workspace packages [ui](../../packages/ui/README.md) and [usdm-schemas](../../packages/usdm-schemas/README.md); `keycloak-js` is loaded on demand.
- Development: Vite, Vitest, Playwright with `@axe-core/playwright`, MSW, `fake-indexeddb`, `jsdom`.
