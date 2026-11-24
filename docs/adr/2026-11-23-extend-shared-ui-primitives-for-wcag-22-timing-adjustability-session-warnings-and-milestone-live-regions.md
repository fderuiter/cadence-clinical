# ADR-2661: Extend Shared UI Primitives for WCAG 2.2 Timing Adjustability, Session Warnings, and Milestone Live Regions

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001 (21 CFR Part 11 session continuity, electronic document verification, and local draft preservation safeguards), PRD-CRF-015 (WCAG 2.2 Level AA SC 2.2.1 Timing Adjustable, SC 2.2.2 Pause, Stop, Hide, and SC 4.1.3 Status Messages)

---

## 1. Context & Problem Statement

Following the modal focus-containment and persistent error-toast unification in [ADR-2655](2026-11-23-unify-focus-coordination-safe-confirmations-and-persistent-toasts.md) and OIDC session resilience in [ADR-2551](2026-11-20-share-session-logout-and-signature-primitives.md), cross-application accessibility audits across `packages/ui`, `apps/web`, and `apps/subject-portal` identified four remaining WCAG 2.2 Level AA gaps under **SC 2.2.1 Timing Adjustable**, **SC 2.2.2 Pause, Stop, Hide**, and **SC 4.1.3 Status Messages**:

1. **Toast Live-Region Semantics & Auto-Dismiss Control:** Routine confirmations in `apps/web` defaulted to `tone: "info"` / `"success"` while still sharing notification timers without global user-configurable timing adjustments (`1x`, `10x` per G180, or manual dismissal `off` per G198), and `apps/subject-portal/src/widgets/toast.js` replaced `#toast-container` inner text in-place under a single `role="status"` element—overwriting concurrent participant confirmations, omitting individual dismiss buttons, and demoting urgent `unbackedActions.js` refusals from `role="alert"` (ARIA19).
2. **Ephemeral Upload Verification Modal (`FileUploadModal.vue`):** Successful SHA-256 verification closed `FileUploadModal.vue` after an unconditional `800ms` timeout without a pre-mounted `role="status"` region or a manual **Done** action, risking missed confirmation announcements under WCAG Failure F103 and violating SC 2.2.1 / Technique G4.
3. **Silent OIDC Session Expiry & Unannounced Login Redirects:** `createOidcSessionCoordinator` (`packages/ui/session.js`) refreshed tokens reactively on `onTokenExpired` and HTTP `401` responses, but provided no pre-expiry warning dialog (`>= 20s` lead per SCR16) or single-action session extension (`>= 10` extensions per SCR1) before involuntary expiry, and `apps/web/src/views/LoginView.vue` rendered no `role="alert"` banner when `authStore.sessionExpired` was `true`.
4. **Per-Second Live-Region Chatter During Offline Sync Backoff:** `apps/subject-portal/src/features/sync-status.js` mutated `#sync-queue-status-text` (`role="status"`, `aria-live="polite"`) every `1000ms` with `Next retry in Ns` countdown ticks—flooding screen-reader speech queues—and offered no **Pause updates** toggle (SC 2.2.2) across `PortalHeader.vue`, `SyncDrawer.vue`, and `TasksView.vue`.

## 2. Decision Drivers & Constraints

- **WCAG 2.2 Level AA Conformance:** Satisfy SC 2.2.1 (Timing Adjustable via G180, G198, SCR16, SCR1), SC 2.2.2 (Pause, Stop, Hide via G4), and SC 4.1.3 (Status Messages via ARIA19 and ARIA22, avoiding F103).
- **Zero-Footprint Auth & Offline Encryption Security:** Keep OIDC bearer tokens strictly in memory, never alter Keycloak realm lifespans or introduce server-side unsigned eCRF draft storage, and preserve ADR-2360 PIN-wrapped AES-GCM IndexedDB drafts and `cadence_user:<tenant_id>:<sub>:` local drafts across involuntary session expiry.
- **Cross-App Primitive Reuse (`packages/ui`):** Share reactive controllers and Vue components between the high-density clinical workspace (`apps/web`) and the 6th-grade reading-level participant companion (`apps/subject-portal`) without duplicating timer or live-region state machines.
- **Background Tab Timer Drift Resilience:** Browser tab throttling clamps `setInterval` / `setTimeout` callbacks in hidden tabs; session expiry warnings must evaluate wall-clock JWT `exp * 1000` against `Date.now()` on `document.visibilitychange` and `window` `focus`.
- **Design System & Focus Containment Invariants:** Preserve `data-focus-trap-exempt="true"` on `ClinicalToastHost.vue` so `useFocusTrap` never marks active notifications `inert`, keep `#sync-queue-list` keyed DOM patching intact so focus on `.site-contact-inline-link` anchors never drops, and use semantic tokens from `packages/ui/tokens.css` exclusively.

## 3. Options Considered

### Option 1: Shared Timing, Toast, Session-Warning, and Milestone Primitives in `packages/ui` (Selected)

- **Overview:**
  - Add `useTimingPreferences` (`standard` `1x`, `extended` `10x`, `off` manual dismissal persisted under `cadence_ui_timing_mode`), `createToastController`, `createMilestoneCountdown`, and `SessionExpiryModal.vue` to `packages/ui`.
  - Split `ClinicalToastHost.vue` into pre-mounted polite (`role="status"`, `aria-live="polite"`, `aria-atomic="false"`) and assertive (`role="alert"`, `aria-live="assertive"`, `aria-atomic="false"`) regions with pointer hover, keyboard focus, and explicit pause/resume controls.
  - Extend `createOidcSessionCoordinator` in `packages/ui/session.js` with wall-clock `warningLeadSeconds` (`60s` default, `>= 20s` floor), `visibilitychange` / `focus` evaluation, and `extendSession()` (`>= 10` extensions via `ensureFreshToken`).
  - Refactor `FileUploadModal.vue` (`5000ms` preference-aware pausable close timer + polite `role="status"` banner + **Done** / **Pause auto-close** controls), `LoginView.vue` (`role="alert"` expired-session banner), and `apps/subject-portal` sync status (`createMilestoneCountdown` + **Pause updates** toggles in `PortalHeader.vue`, `SyncDrawer.vue`, and `TasksView.vue`).
- **Pros:**
  - Centralizes timing multipliers, pause/resume bookkeeping, live-region role routing, wall-clock session expiry checks, and discrete milestone announcements in tested `packages/ui` primitives.
  - Eliminates live-region speech chatter while keeping visual tabular-numeral countdowns (`aria-hidden="true"`) responsive for sighted users.
  - Preserves backward-compatible imperative toast and sync helpers in `apps/subject-portal`.
- **Cons:**
  - Increases `packages/ui` surface area by one component (`SessionExpiryModal.vue`) and three composables (`useTimingPreferences`, `createToastController`, `createMilestoneCountdown`).

### Option 2: App-Local Timer Patches and `aria-live="off"` Overrides

- **Overview:** Patch toast timeouts separately in `apps/web` and `apps/subject-portal`, silence `#sync-queue-status-text` completely during retries, and rely solely on post-expiry redirects.
- **Pros:**
  - Smaller initial diff in `packages/ui`.
- **Cons:**
  - Fails WCAG 2.2 SC 2.2.1 for authenticated session warnings (SCR16/SCR1) and global timing adjustability (G180/G198), duplicates hover/focus timer math across apps, and hides retry attempt milestones from assistive technology users.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Consolidating WCAG 2.2 timing and live-announcement primitives in `packages/ui` ensures uniform assistive-technology semantics, shared user timing preferences across clinical and participant surfaces, wall-clock accuracy across backgrounded tabs, and zero regression to Part 11 / offline encryption boundaries.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - Routine confirmations across all 29 `apps/web` call sites and `apps/subject-portal` announce politely via `role="status"` (ARIA22), while API and unbacked-action errors announce assertively via `role="alert"` (ARIA19) and persist until dismissed.
  - Users can pause timed notifications and upload auto-close timers via hover, keyboard focus, or explicit pause controls (G4), or switch globally between `1x`, `10x` (G180), and `Off` (G198) in `AppShell.vue` and `ParticipantCard.vue`.
  - Authenticated sessions warn `60s` (`>= 20s` floor) before token expiry via `SessionExpiryModal.vue` (`role="alertdialog"`) with repeatable single-action extensions (`extendSession`), and `LoginView.vue` announces involuntary session expiry and preserved local drafts via `role="alert"`.
  - Subject portal sync retries announce discrete attempt/pause/resume/completion/failure milestones once per transition while exposing **Pause updates** in `PortalHeader.vue`, `SyncDrawer.vue`, and `TasksView.vue`.
- **Negative Impact / Technical Debt:**
  - Pre-mounted polite and assertive live containers remain present in `ClinicalToastHost.vue` even when empty so browsers register live regions prior to dynamic insertion.
- **Mitigation Strategy:**
  - Hide empty toast regions visually (`pointer-events: none`) while preserving AT live-region registration, and enforce behavior via Vitest unit suites in `packages/ui`, `apps/web`, and `apps/subject-portal` plus Playwright verification.

## 6. Implementation & Verification

- **Affected Packages & Applications:**
  - `packages/ui`: `ClinicalToastHost.vue`, `SessionExpiryModal.vue`, `FileUploadModal.vue`, `createToastController.js`, `useTimingPreferences.js`, `createMilestoneCountdown.js`, `session.js`, `index.js`, `src/index.js`, `index.d.ts`.
  - `apps/web`: `stores/toasts.js`, `components/ToastHost.vue`, `components/AppShell.vue`, `auth/sessionCoordinator.js`, `views/LoginView.vue`.
  - `apps/subject-portal`: `widgets/toast.js`, `App.vue`, `components/ParticipantCard.vue`, `components/PortalHeader.vue`, `components/SyncDrawer.vue`, `views/TasksView.vue`, `features/auth.js`, `features/sync-status.js`, `features/sync.js`, `stores/session.js`, `stores/sync.js`, `index.js`.
- **Verification Plan:**
  - Unit and accessibility suites in `packages/ui/tests/wcag_timing_and_live_announcements.test.js`, `apps/web/tests/wcag_timing_and_session_alerts.spec.js`, and `apps/subject-portal/tests/wcag-timing-toasts-and-sync-milestones.test.js`, plus workspace typecheck, demo bundle budget checks, and Cadence CLI gates (`cadence check adrs`, `markdown`, `readmes`, `public-bundle`, `demo-manifest`).
