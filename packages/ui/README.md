# ui

Shared frontend package (JavaScript, TypeScript types and Vue 3 components) used by the web client and the subject portal.

## Contents

- `index.js` (main entry, resolved directly from workspace source by consuming apps and built to `dist/` as an ES and CJS library): helpers such as `debounce`, `createSrOnlyActionButton` (accessible DOM button builder appending `.sr-only` context while preserving leading visible verbs per WCAG 2.2 SC 2.5.3), HTML builders for rule editors, condition-tree serialisation, `initHoverDetection`, and re-exports of the modules and components below.
- `signing.js`: browser-side hashing, canonical serialisation, AES-GCM helpers (`sha256`, `canonicalSerialize`, `encryptAESGCM`, `deriveKeyFromPIN`), and 21 CFR Part 11 v3 step-up helpers (`SEMANTIC_ACTIONS`, `STEP_UP_VERIFICATION_PATH`, `isValidSemanticAction`, `buildStepUpVerificationPayload`, `formatCgtBedsideBindingId`, `formatCgtDestructionBindingId`, `resolveDefaultSemanticAction`, `requestStepUpSignature`, `SIGNATURE_PASSWORD_INPUT_ATTRS`, `requiresCustomSignatureReason`, `resolveSignatureReason`, `deriveSignerRoleFromClaims`).
- `session.js`: shared OIDC session coordinator (`createOidcSessionCoordinator`) providing `onTokenExpired` / `onAuthRefreshSuccess` registration, single-flight `updateToken` deduplication, one-shot `401` refresh-and-retry, draft-preserving involuntary expiry transitions, and safe same-origin return-URL persistence (ADR-2551).
- `callerScope.js`: framework-neutral OIDC claim and scope helpers (`parseClaimStringList`, `decodeJwtPayloadClaims`, `extractAssignedStudies`, `extractAssignedSites`, `extractPrimaryStudyId`, `extractPrimarySiteId`, `isForbiddenScopeError`, `formatScopeForbiddenGuidance`) shared by `apps/web` and `apps/subject-portal` so `studyId` and `siteId` derive deterministically from token claims (`study_id`, `study_ids`, `site_id`, `site_ids`) without hardcoded fallback identifiers ([ADR-2657](../../docs/adr/2026-11-23-share-ui-primitives-and-query-synced-portal-routes.md)).
- `mockGateway.js`, exported only as `ui/testing`: the gateway simulator and MSW service-worker coordination helpers for static demo builds and tests (`validateGatewayRequest`, `isGatewayApiCall`, `isPathSignatureGated`, `normalizeMswBasePath`, `resolveMswServiceWorkerOptions`, `waitForMswController`, `ensureServiceWorkerController`, `createMswWorkerStarter`, `evaluateDemoBoundaryRequest`). It imports neither `msw` nor `msw/browser`, is not part of the root export, and holds no secret: it verifies an `X-Gateway-Signature` only against a secret the caller passes as `options.secret`, and rejects one it cannot check. Real authentication happens in [apps/gateway](../../apps/gateway/README.md).
- `econsent.js`: consent normalisation and comprehension-check helpers.
- `accessibility-matcher.js`, exported only as `ui/vitest` (typed via `accessibility-matcher.d.ts`): `toBeAccessible` Vitest matcher built on `axe-core` ([ADR-2537](../../docs/adr/2026-11-22-split-ui-vitest-matcher-and-enforce-demo-bundle-budgets.md)). It is excluded from the root `ui` runtime bundle so `axe-core` never ships in production or demo builds.
- `src/components/`: 24 shared Vue components re-exported from the root `ui` entry:
  - `ClinicalBreadcrumbs` (including `compactMobile` single-row `< 640px` mode per [ADR-2686](../../docs/adr/2026-11-24-tablet-icon-rail-and-compact-portal-chrome.md))
  - `ClinicalContextBar`
  - `ClinicalDataTable` (client/external/none pagination, controlled sort, focusable or masked rows, and header/cell/pagination slots per [ADR-2656](../../docs/adr/2026-11-23-clinical-data-table-keyboard-and-aria-compliance.md) and [ADR-2673](../../docs/adr/2026-11-24-centralize-css-states-and-migrate-live-clinical-hubs.md))
  - `ClinicalDrawer` (side drawer, desktop docked inspector, plus `position="bottom"` / `mobileBottomSheet` `<= 767px` `90dvh` safe-area bottom sheet per [ADR-2686](../../docs/adr/2026-11-24-tablet-icon-rail-and-compact-portal-chrome.md))
  - `ClinicalDropzone`
  - `ClinicalExplanation`
  - `ClinicalFieldLayout` (`touch` and `compact` densities, required markers, hints, and status alerts)
  - `ClinicalFormField` (auto-propagating `data-tabular-nums="true"` for numeric/date CDASH fields)
  - `ClinicalGatedAction`
  - `ClinicalIcon`
  - `ClinicalIconButton`
  - `ClinicalInput` (`48px` touch or `compact` `36px` staff `<input>`, `<select>`, and `<textarea>` controls with `16px` coarse/mobile font sizing plus `type`, `inputmode`, and `tabularNums` / `data-tabular-nums` alignment)
  - `ClinicalLookupInput`
  - `ClinicalModal` (with `@media (max-height: 480px)` `calc(100dvh - 16px)` flex-column scrolling `.modal-body` and pinned `.modal-header` / `.modal-footer` per [ADR-2686](../../docs/adr/2026-11-24-tablet-icon-rail-and-compact-portal-chrome.md))
  - `ClinicalQueryFlag`
  - `ClinicalQueryPanel`
  - `ClinicalRadioGroup`
  - `ClinicalTabs` (`underline`, `pills`, and `segmented` variants, `scroll`/`wrap` overflow modes with edge fade cues, `44px` hit targets, and reduced-motion-aware focus scroll alignment per [ADR-2659](../../docs/adr/2026-11-23-centralize-clinical-tabs-variants-overflow-and-scoped-css-gates.md))
  - `ClinicalToastHost`
  - `common/EmptyState`
  - `EntityLink`
  - `MediaPlayer`
  - `ModalErrorAlert`
  - `SignOutConfirmModal`
- `src/composables/`: reactive `useFocusTrap(targetRef, options)` (focus containment, LIFO nested overlay stack, subtree `MutationObserver` active-element detachment recovery so `v-if` swaps inside open dialogs/drawers never strand focus on `document.body`, background sibling `inert`/`aria-hidden="true"` reconciliation with `[data-focus-trap-exempt="true"]` live-alert exemptions and `Tab` bridging, `Escape` dismissal or lock-screen blocking, and trigger focus restoration with `options.fallbackFocus`, parent-trap stack fallback, and visible fallback focus restoration when triggers unmount or hide per [ADR-2676](../../docs/adr/2026-11-24-focus-trap-detachment-recovery-and-retry-focus-primitives.md)), `hasActiveModalFocusTrap(options)`, `getTopActiveModalContainer()`, `handoffFocus(targets, options)` and `useInlineFocusHandoff(options)` (non-trapping inline focus handoff, visibility/inert verification after `nextTick`, `tabindex="-1"` programmatic heading handoff, and trigger restoration across `v-if` swaps and portal view transitions), `handoffRetryFocus(action, options)` and `useRetryFocusHandoff(options)` (focus preservation across inline async retry buttons that unmount during loading and either recover into loaded content or re-render an error alert), `useEscapeClose(onClose, options)` (LIFO topmost dismissal for non-trapped overlays), `getFocusableElements(container)`, `trapTabFocus(event, container)`, and `useSignOutGuard` (flushes debounced drafts and requires explicit confirmation before destructive sign-out cleanup when unsaved work exists), all re-exported from the root `ui` entry. `src/types/cdash.ts`: CDASH types.
- `tokens.css`, `responsive.css`: centralized light, dark (`[data-theme="dark"]`, `.dark`), and system-auto (`[data-theme="auto"]`) WCAG 2.2 Level AA calibrated semantic design tokens with 1:1 dark/auto parity across all non-inverse color, badge, backdrop, watermark, and shadow primitives ([ADR-2675](../../docs/adr/2026-11-23-standardize-semantic-role-tokens-and-eslint-color-gates.md)) and shared application aliases (`--primary`, `--primary-dark`, `--primary-light`, `--accent`, `--accent-light`, `--accent-bg`, `--text-main`, `--text-muted`, `--bg-main`, `--bg-card`, `--neutral-dark`, `--neutral-light`, `--border`, `--border-control`, `--success`, `--success-bg`, `--warning`, `--warning-bg`, `--error`, `--error-bg`, `--danger`, `--danger-bg`, `--font`, `--semantic-font-mono`, `--font-variant-numeric-tabular`, and `--semantic-font-variant-numeric-tabular`, separating neutral primary ink `--text-main` / `--semantic-color-text` from interactive brand blue `--primary` / `--semantic-color-primary`; semantic text, GxP/readiness/workflow/regulatory badge [`.clinical-badge--draft`, `--verified`, `--queried`, `--frozen`, `--signed`, `--locked`, `--warning`, `--error`, `--unsaved`, `--candidate`, `--in-progress`, `--regulatory` backed by `--semantic-badge-*` tokens at a `0.75rem` tabular-numeral floor per ADR-2654, ADR-2671, and ADR-2675], edited-row surface and accent border tokens [`--semantic-color-edited-row-bg`, `--semantic-color-edited-row-border` delivering `>= 3:1` non-text and `>= 4.5:1` text contrast], solid interactive danger fills `--semantic-color-error-interactive{,-hover}`, `--semantic-color-text-on-accent`, `--semantic-color-imaging-highlight`, translucent `--semantic-color-backdrop{,-soft,-strong}` / `--semantic-color-surface-glass` / `--semantic-color-inverse-tint{,-strong}` overlays, and status tokens delivering `>= 4.5:1` contrast, `--color-border-control` / `--semantic-color-border-control` delivering `>= 3:1` interactive control boundary contrast per SC 1.4.11 while preserving `--semantic-color-border` for passive dividers, pointer target tokens `--control-target-min: 24px` / `--semantic-control-target-min` per SC 2.5.8 [covering compact controls and `<summary>` disclosures] alongside `--touch-target-min: 48px` / `--semantic-touch-target-min` per SC 2.5.5, a shared low-specificity two-color `:focus-visible` indicator pairing a `2px` `--semantic-color-text-on-solid` inner halo with a `3px` `--semantic-color-accent` outer outline plus `:not([tabindex="-1"])` programmatic landmark/heading focus ring suppression, `:where([tabindex="-1"]):focus:not(:focus-visible)` programmatic focus suppression, and `@media (forced-colors: active)` `Highlight` outlines per ADR-2593, ADR-2646, and ADR-2673, centralized `.btn:disabled` / `.btn[aria-disabled="true"]` and `:not(:disabled)` hover guards, wallet gradient tokens `--semantic-color-wallet-start` and `--semantic-color-wallet-end`, viewport-relative drawer width tiers `--drawer-width-sm` [440px], `--drawer-width-md` [480px], `--drawer-width-lg` [640px], `--drawer-width-xl` [920px] and semantic mappings `--semantic-drawer-width-*`, the AI output colour `--semantic-color-ai`, `--semantic-color-ai-bg` and `--semantic-color-ai-border` reserved exclusively for agent output per UX-8 and ADR-2635, global `*` `box-sizing` reset, `.form-actions` layout, `.btn-primary` brand colors, `.sr-only` visually hidden screen-reader utility class, shared `.clinical-visit-matrix`, `.clinical-soa-matrix`, and `.clinical-lookup-container` styles, shared `.font-mono` [`--semantic-font-mono` with platform-first `"JetBrains Mono"` / `ui-monospace` stack], `.numeric`, `.tabular-nums`, and a `12px` [`--font-size-2xs`] minimum typography floor, global `font-variant-numeric: tabular-nums` / `var(--font-variant-numeric-tabular)` rules covering all application tables without per-component `<style scoped>` copies, `<time>`, `.clinical-badge`, `.tabular-nums`, `.numeric`, `[data-tabular-nums]`, and UA-reset numeric/date `<input>` and `<select>` controls while keeping `<textarea>` proportional and `@media (pointer: coarse)` form controls `>= 16px` (`1rem`) per ADR-2650, ADR-2670, and [ADR-2824](../../docs/adr/2026-11-24-standardize-clinical-typography-utilities-12px-floor-and-ast-lints.md), and global `@media (prefers-reduced-motion: reduce)` rules) and responsive utilities (`minmax(0, 1fr)` `.responsive-grid`, `.grid-layout-responsive`, `.grid-2-responsive`, and `.grid-asymmetric-responsive` helpers with `< 1024px` nested `fieldset` span resets, `position: relative` `.scrollable-table-wrapper` containment, `min-width: 0` `.clinical-input` shrink safety and `flex-shrink: 0` `.query-flag` preservation per ADR-2665, `48px` `.touch-target`, `.min-touch-target`, and `.touch-target-interactive` helpers, `.btn-sm` `24x24px` compact target floor, `.drawer-mobile-full` and `.drawer-panel-mobile-full` for <=640px viewports, and `.drawer-bottom-sheet`, `.drawer-panel-bottom-sheet`, `.drawer-mobile-bottom-sheet`, and `.drawer-panel-mobile-bottom-sheet` `90dvh` safe-area bottom-sheet utilities) (exported as `ui/tokens.css`, `ui/responsive.css`).
- `index.d.ts`: type declarations; `scripts/generate-types.js` runs before the build.

## Scripts

| Command                      | Purpose                                                             |
| ---------------------------- | ------------------------------------------------------------------- |
| `pnpm --filter ui build`     | Generates types, then builds the library with Vite.                 |
| `pnpm --filter ui test`      | Vitest (`tests/`) with coverage.                                    |
| `pnpm --filter ui lint`      | ESLint.                                                             |
| `pnpm --filter ui typecheck` | Strict `vue-tsc --noEmit` across `index.d.ts`, Vue SFCs, and tests. |

## Coverage

`pnpm --filter ui test` measures coverage over every source file, components included (ADR-2530). The global thresholds
in `vite.config.js` are a ratchet floor: raise them when coverage rises, never lower them. `signing.js` and
`econsent.js`, the Part 11 signing and eConsent helpers, carry an 80 percent gate of their own.

## Dependencies

- Runtime: peer dependency `vue` and externalized `lucide-vue-next` iconography (no runtime npm packages bundled into `dist/`).
- Development: Vite, Vitest with `@vitest/coverage-v8`, `axe-core` (backing `ui/vitest`), TypeScript, `vue-tsc`, `pinia`.

## Used by

[apps/web](../../apps/web/README.md) and [apps/subject-portal](../../apps/subject-portal/README.md) (`"ui": "workspace:*"`). Both apps resolve `ui`, `ui/testing`, `ui/vitest`, `ui/tokens.css`, and `ui/responsive.css` directly from workspace source during local development, Vitest runs, and application builds ([ADR-2658](../../docs/adr/2026-11-23-resolve-shared-ui-from-source-and-align-verification-scripts.md)); `pnpm --filter ui build` remains available to regenerate `index.d.ts` and validate the library bundle.

### Inline Explanation & Gated Action Primitives

- `EntityLink`: Renders Clinical Kernel `NodeRef` links in monospace with K-EXP-2 masked badge support and scope-preserving peek-drawer activation.
- `ClinicalExplanation`: Renders ADR-2482 `Explanation` claims, evaluated met/unmet conditions, reasoning steps, and clickable `NextAction.Target` links.
- `ClinicalGatedAction`: Keeps blocked clinical controls visible with accessible why-blocked explanations.

### Bounded Drawer & Mobile Record-Card Primitives

- `ClinicalDrawer`: Bounded-chrome `100dvh` side drawer (`#header` + scrollable `.clinical-drawer-body` + optional `#footer`), `keepMounted` state retention, `@media (max-height: 540px)` compact short-viewport scrolling, `mobileBottomSheet` `<= 767px` bottom sheet, and `@media (max-width: 480px)` `90dvh` safe-area bottom sheet ([ADR-2674](../../docs/adr/2026-11-24-migrate-web-drawers-to-bounded-clinical-drawer.md), [ADR-2686](../../docs/adr/2026-11-24-tablet-icon-rail-and-compact-portal-chrome.md)).
- `ClinicalDataTable`: Emits `:data-label="col.label"` on body cells so narrow mobile drawers (`<= 480px` / `<= 640px`) can style `attr(data-label)` stacked record cards without raw `<table>` markup.
