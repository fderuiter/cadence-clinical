# ui

Shared frontend package (JavaScript, TypeScript types and Vue 3 components) used by the web client and the subject portal.

## Contents

- `index.js` (main entry, built to `dist/` as an ES and CJS library): helpers such as `debounce`, HTML builders for lookup inputs, rule editors and schedule-of-activities matrices, condition-tree serialisation, `initHoverDetection`, and re-exports of the modules and components below.
- `signing.js`: browser-side hashing, canonical serialisation and AES-GCM helpers (`sha256`, `canonicalSerialize`, `encryptAESGCM`, `deriveKeyFromPIN`, and others).
- `mockGateway.js`: gateway path and signature helpers for mocks and tests (`validateGatewayRequest`, `isPathSignatureGated`, a test `GATEWAY_SECRET`). It is not used for real authentication, which happens in [apps/gateway](../../apps/gateway/README.md).
- `econsent.js`: consent normalisation and comprehension-check helpers.
- `accessibility-matcher.js`: `toBeAccessible` Vitest matcher built on `axe-core`.
- `src/components/`: Vue components (`FileUploadModal`, `MediaPlayer`, and `clinical/` components such as `ClinicalFormField`, `ClinicalDataTable`, `ClinicalQueryPanel`, `AuditLogViewer`, `PersonaSwitcher`).
- `src/composables/`: `useFocusTrap`, `useEscapeClose`. `src/types/cdash.ts`: CDASH types.
- `tokens.css`, `responsive.css`: design tokens (including viewport-relative drawer width tiers `--drawer-width-sm` [440px], `--drawer-width-md` [480px], `--drawer-width-lg` [640px], `--drawer-width-xl` [920px] and semantic mappings `--semantic-drawer-width-*`) and responsive utilities (`.drawer-mobile-full` and `.drawer-panel-mobile-full` for <=640px viewports) (exported as `ui/tokens.css`, `ui/responsive.css`).
- `index.d.ts`: type declarations; `scripts/generate-types.js` runs before the build.

## Scripts

| Command                  | Purpose                                             |
| ------------------------ | --------------------------------------------------- |
| `pnpm --filter ui build` | Generates types, then builds the library with Vite. |
| `pnpm --filter ui test`  | Vitest (`tests/`).                                  |
| `pnpm --filter ui lint`  | ESLint.                                             |

## Dependencies

- Runtime: `axe-core`; peer dependency `vue`.
- Development: Vite, Vitest, TypeScript, `pinia`.

## Used by

[apps/web](../../apps/web/README.md) and [apps/subject-portal](../../apps/subject-portal/README.md) (`"ui": "workspace:*"`). Both apps build it before their own build and test runs.
