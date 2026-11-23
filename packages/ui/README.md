# ui

Shared frontend package (JavaScript, TypeScript types and Vue 3 components) used by the web client and the subject portal.

## Contents

- `index.js` (main entry, built to `dist/` as an ES and CJS library): helpers such as `debounce`, `createSrOnlyActionButton` (accessible DOM button builder appending `.sr-only` context while preserving leading visible verbs per WCAG 2.2 SC 2.5.3), HTML builders for lookup inputs, rule editors and schedule-of-activities matrices, condition-tree serialisation, `initHoverDetection`, and re-exports of the modules and components below.
- `signing.js`: browser-side hashing, canonical serialisation, AES-GCM helpers (`sha256`, `canonicalSerialize`, `encryptAESGCM`, `deriveKeyFromPIN`), and 21 CFR Part 11 v3 step-up helpers (`SEMANTIC_ACTIONS`, `isValidSemanticAction`, `buildStepUpVerificationPayload`, `SIGNATURE_PASSWORD_INPUT_ATTRS`, `requiresCustomSignatureReason`, `resolveSignatureReason`, `deriveSignerRoleFromClaims`).
- `session.js`: shared OIDC session coordinator (`createOidcSessionCoordinator`) providing `onTokenExpired` / `onAuthRefreshSuccess` registration, single-flight `updateToken` deduplication, one-shot `401` refresh-and-retry, draft-preserving involuntary expiry transitions, and safe same-origin return-URL persistence (ADR-2551).
- `mockGateway.js`, exported only as `ui/testing`: the gateway simulator and MSW service-worker coordination helpers for static demo builds and tests (`validateGatewayRequest`, `isGatewayApiCall`, `isPathSignatureGated`, `normalizeMswBasePath`, `resolveMswServiceWorkerOptions`, `waitForMswController`, `ensureServiceWorkerController`, `createMswWorkerStarter`, `evaluateDemoBoundaryRequest`). It imports neither `msw` nor `msw/browser`, is not part of the root export, and holds no secret: it verifies an `X-Gateway-Signature` only against a secret the caller passes as `options.secret`, and rejects one it cannot check. Real authentication happens in [apps/gateway](../../apps/gateway/README.md).
- `econsent.js`: consent normalisation and comprehension-check helpers.
- `accessibility-matcher.js`: `toBeAccessible` Vitest matcher built on `axe-core`.
- `src/components/`: 22 shared Vue components re-exported from the root `ui` entry (`FileUploadModal`, `MediaPlayer`, `ModalErrorAlert`, `common/EmptyState`, and `clinical/` components `AiActionButton`, `AiSuggestionDrawer`, `AuditLogViewer`, `ClinicalDataTable`, `ClinicalDrawer`, `ClinicalDropzone`, `ClinicalFieldLayout`, `ClinicalFormField`, `ClinicalIconButton`, `ClinicalInput`, `ClinicalLookupInput`, `ClinicalModal`, `ClinicalQueryFlag`, `ClinicalQueryPanel`, `ClinicalRadioGroup`, `ConfidenceBadge`, `PersonaSwitcher`, `SignOutConfirmModal`).
- `src/composables/`: reactive `useFocusTrap(targetRef, options)` (focus containment, LIFO nested overlay stack, background sibling `inert`/`aria-hidden="true"` reconciliation with `[data-focus-trap-exempt="true"]` live-alert exemptions, `Escape` dismissal or lock-screen blocking, and trigger focus restoration), `useEscapeClose(onClose, options)` (LIFO topmost dismissal for non-trapped overlays), `getFocusableElements(container)`, `trapTabFocus(event, container)`, `useAsyncRetry`, and `useSignOutGuard` (flushes debounced drafts and requires explicit confirmation before destructive sign-out cleanup when unsaved work exists), all re-exported from the root `ui` entry. `src/types/cdash.ts`: CDASH types.
- `tokens.css`, `responsive.css`: centralized light and dark (`[data-theme="dark"]`, `.dark`, `[data-theme="auto"]`) WCAG 2.2 Level AA calibrated semantic design tokens (semantic text and status tokens delivering `>= 4.5:1` contrast, `--color-border-control` / `--semantic-color-border-control` delivering `>= 3:1` interactive control boundary contrast per SC 1.4.11 while preserving `--semantic-color-border` for passive dividers, wallet gradient tokens `--semantic-color-wallet-start` and `--semantic-color-wallet-end`, viewport-relative drawer width tiers `--drawer-width-sm` [440px], `--drawer-width-md` [480px], `--drawer-width-lg` [640px], `--drawer-width-xl` [920px] and semantic mappings `--semantic-drawer-width-*`, the AI output colour `--semantic-color-ai`, `--semantic-color-ai-bg` and `--semantic-color-ai-border` reserved exclusively for agent output per UX-8 and ADR-2635, global `.sr-only` visually hidden screen-reader utility class, global `font-variant-numeric: tabular-nums` table rules, and global `@media (prefers-reduced-motion: reduce)` rules) and responsive utilities (`.drawer-mobile-full` and `.drawer-panel-mobile-full` for <=640px viewports) (exported as `ui/tokens.css`, `ui/responsive.css`).
- `index.d.ts`: type declarations; `scripts/generate-types.js` runs before the build.

## Scripts

| Command                  | Purpose                                             |
| ------------------------ | --------------------------------------------------- |
| `pnpm --filter ui build` | Generates types, then builds the library with Vite. |
| `pnpm --filter ui test`  | Vitest (`tests/`) with coverage.                    |
| `pnpm --filter ui lint`  | ESLint.                                             |

## Coverage

`pnpm --filter ui test` measures coverage over every source file, components included (ADR-2530). The global thresholds
in `vite.config.js` are a ratchet floor: raise them when coverage rises, never lower them. `signing.js` and
`econsent.js`, the Part 11 signing and eConsent helpers, carry an 80 percent gate of their own.

## Dependencies

- Runtime: `axe-core`; peer dependency `vue`.
- Development: Vite, Vitest with `@vitest/coverage-v8`, TypeScript, `pinia`.

## Used by

[apps/web](../../apps/web/README.md) and [apps/subject-portal](../../apps/subject-portal/README.md) (`"ui": "workspace:*"`). Both apps build it before their own build and test runs.
