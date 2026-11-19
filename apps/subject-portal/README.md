# Subject portal

Participant-facing eCOA/ePRO and eConsent web app (Vue 3, Vite) with an offline sync queue and a service worker. In the Vite dev server it calls its own origin, and the dev server's `/api` proxy forwards to the API gateway (`CADENCE_GATEWAY_URL`, default `http://localhost:8000`), because the gateway sends no CORS headers (#5451). The AppHost's `full` profile runs it on port 5174.

## Layout

| Path                    | Contents                                                                                                                                                                                                                                    |
| ----------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `src/index.js`          | Entry point loaded by `index.html`: runs `initializeApp` on `DOMContentLoaded` and re-exports the portal's public API (`state`, `showView`, `syncOfflineQueue`, ...), which the tests import from here.                                     |
| `src/App.vue`           | The shell: header, sync drawer, tab bar, participant card, banners, every view, the modals and the audit ledger footer.                                                                                                                     |
| `src/views/`            | One component per view (`TasksView`, `QuestionnaireView`, `ComplianceView`, `InboxView`, `ConsentView`, `WalletView`, `LaySummariesView`, `UnavailableView`); each is the view's `<section class="portal-view">`.                           |
| `src/components/`       | Reusable pieces: `PortalHeader`, `DemoBanner`, `SyncDrawer`, `TabBar`, `ParticipantCard`, `ReconsentBanner`, `SyncSuspendedBanner`, `SignatureModal`, `ReconsentModal`, `PinSetupModal`, `PinUnlockModal`, `LedgerFooter`, `focus-trap.js`. |
| `src/router/`           | `routes.js` (one route per view), `availability.js` (screen availability guards outside demo builds), and `index.js` (the router and its URL and view synchronisation).                                                                     |
| `src/stores/`           | Pinia stores (`session`, `ui`, `tasks`, `compliance`, `inbox`, `consent`, `sync`, `wallet`, `audit`), the shared Pinia instance and `state.js`, the `state` facade over them.                                                               |
| `src/features/`         | One module per feature: `tasks`, `questionnaire`, `signature`, `compliance`, `inbox`, `consent`, `reconsent`, `sync`, `sync-status`, `pin`, `auth` (cached identity, Keycloak, logout), `wallet`, `audit`, `navigation`.                    |
| `src/utils/`            | `demoMode.js` (explicit `demo` mode / `VITE_DEMO_MODE=true` checks) and `unbackedActions.js` (runtime guards refusing unpersisted actions outside demo builds).                                                                             |
| `src/api/client.js`     | `dispatchApi` and the typed fetch helpers.                                                                                                                                                                                                  |
| `src/bootstrap/`        | `initialize.js` (`initializeApp`), `mount.js` (mounts the shell with Pinia and the router), `controls.js` (button bindings and the tab list's ARIA and keyboard handling).                                                                  |
| `src/widgets/`          | HTML-string form widgets (`clinical-fields.js`), inline field errors and the toast.                                                                                                                                                         |
| `src/data/mock-data.js` | Synthetic demo data loaded only in explicit `demo` builds (`--mode demo` or `VITE_DEMO_MODE=true`).                                                                                                                                         |
| `src/sync-queue.js`     | IndexedDB-backed offline submission queue encrypted via an ADR-2360 memory-only PIN-derived key (never persisted unwrapped in IndexedDB).                                                                                                   |
| `src/mocks/`            | MSW handlers, started only in explicit `demo` builds (`--mode demo`, `VITE_DEMO_MODE=true`, or `VITE_ENABLE_MSW=true`).                                                                                                                     |
| `src/style.css`         | Styles.                                                                                                                                                                                                                                     |
| `public/`               | `manifest.json`, `sw.js` (service worker), `mockServiceWorker.js` (MSW).                                                                                                                                                                    |
| `tests/`, `tests/e2e/`  | Vitest unit tests; Playwright specs.                                                                                                                                                                                                        |

### State, router and views

- **State.** The Pinia stores hold the portal's state. The feature modules (and the tests) use `state` from `src/stores/state.js`: one object whose top-level keys (`state.session`, `state.assignments`, `state.currentView`, ...) read and write the store that declares them, so it behaves like the single reactive object the portal used to keep. Components use the stores directly.
- **Views stay mounted.** The feature modules render tasks, compliance, the inbox, consent and the sync queue into elements by id (`tasks-list-container`, `compliance-history-tbody`, ...), so all views are always in the DOM; `state.currentView` decides which one has the `active` class and which tab is selected.
- **Router & availability guards.** `vue-router` with `createWebHistory("/subject-portal/")` gives each view a URL: `/` (tasks), `/questionnaire`, `/compliance`, `/inbox`, `/consent`, `/wallet`, `/lay-summaries`, and `/unavailable`. Outside explicit `demo` builds, unbacked tabs (`wallet`, `lay-summaries`) are hidden from navigation and direct visits redirect to `/unavailable` (`UnavailableView.vue`) with a return action to My Tasks. A deep link to `/questionnaire` without a questionnaire in progress opens My Tasks.
- **PIN-wrapped offline encryption (ADR-2360).** Queued ePRO submissions and drafts are AES-GCM encrypted in per-user IndexedDB (`SubjectPortalSyncDB_${userId}`). Only the PBKDF2-wrapped master key is stored in `sync_config`; the unwrapped key lives strictly in JavaScript memory after PIN setup or PIN unlock and is cleared on page reload, user switch, or sign-out. `PinUnlockModal` provides keyboard-accessible Sign Out and confirmed Reset PIN recovery options.

## Backend and authentication

- Requests go to `<base>/api/v1/...`; paths without an `api/v1/` prefix are sent to `/api/v1/interop/...`. The base is `VITE_API_BASE_URL`, read in `src/api/client.js`: the dev server's own origin (`http://localhost:5174`) in `vite` dev unless set, and `http://localhost:8000` in builds and tests. Do not point it at the gateway from the dev server: the browser blocks the cross-origin call.
- When Keycloak sign-in succeeds (`VITE_KEYCLOAK_URL`, `VITE_KEYCLOAK_REALM`, `VITE_KEYCLOAK_CLIENT_ID`; defaults `http://localhost:8080/`, `cadence`, `cadence-web`), the access token is sent as `Authorization: Bearer <token>`. The dev realm's `cadence-web` client allows the portal's redirect URI, `http://localhost:5174/subject-portal/*` (`docker/cadence-realm.json`). Without a token in production mode, the portal prompts the user to sign in and never populates synthetic study data.
- Electronic consent and protocol amendment re-consent require live step-up verification (`/api/v1/auth/signature-verification`) and backend persistence outside `demo` builds; offline symptom diaries remain in `PENDING_SYNC` (`Pending Sync`) until `/api/v1/interop/epro/sync` confirms persistence.

## Scripts

Run from the repository root with pnpm:

| Command                                   | Purpose                                                            |
| ----------------------------------------- | ------------------------------------------------------------------ |
| `pnpm --filter subject-portal dev`        | Vite dev server on `http://localhost:5174/subject-portal/`.        |
| `pnpm --filter subject-portal dev:demo`   | Vite dev server in explicit `demo` mode with synthetic study data. |
| `pnpm --filter subject-portal build`      | Production build. Build `ui` first (`pnpm --filter ui build`).     |
| `pnpm --filter subject-portal build:demo` | Demo build (`--mode demo`) with explicit synthetic data banner.    |
| `pnpm --filter subject-portal test`       | Vitest with coverage (80% thresholds on `src/sync-queue.js`).      |
| `pnpm --filter subject-portal test:e2e`   | Playwright.                                                        |
| `pnpm --filter subject-portal lint`       | ESLint.                                                            |

`docker/docker-compose.yml` also runs the dev server as the `subject-portal` service on port 5174.

## Dependencies

- Runtime: `vue`, `vue-router`, `pinia` and the workspace packages [ui](../../packages/ui/README.md) and [usdm-schemas](../../packages/usdm-schemas/README.md); `keycloak-js` is loaded on demand.
- Development: Vite, Vitest, Playwright with `@axe-core/playwright`, MSW, `fake-indexeddb`, `jsdom`.
