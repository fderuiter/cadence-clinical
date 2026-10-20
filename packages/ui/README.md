# ui

Shared frontend package (JavaScript, TypeScript types and Vue 3 components) used by the web client and the subject portal.

## Contents

- `index.js` (main entry, built to `dist/` as an ES and CJS library): helpers such as `debounce`, HTML builders for lookup inputs, rule editors and schedule-of-activities matrices, condition-tree serialisation, `initHoverDetection`, and re-exports of the modules and components below.
- `signing.js`: browser-side hashing, canonical serialisation and AES-GCM helpers (`sha256`, `canonicalSerialize`, `encryptAESGCM`, `deriveKeyFromPIN`, and others).
- `mockGateway.js`, exported only as `ui/testing`: the gateway simulator for MSW handlers and tests (`validateGatewayRequest`, `isGatewayApiCall`, `isPathSignatureGated`). It is not part of the root export and holds no secret: it verifies an `X-Gateway-Signature` only against a secret the caller passes as `options.secret`, and rejects one it cannot check. Real authentication happens in [apps/gateway](../../apps/gateway/README.md).
- `econsent.js`: consent normalisation and comprehension-check helpers.
- `accessibility-matcher.js`: `toBeAccessible` Vitest matcher built on `axe-core`.
- `src/components/`: Vue components (`FileUploadModal`, `MediaPlayer`, and `clinical/` components such as `ClinicalFormField`, `ClinicalDataTable`, `ClinicalQueryPanel`, `AuditLogViewer`, `PersonaSwitcher`).
- `src/composables/`: `useFocusTrap`, `useEscapeClose`. `src/types/cdash.ts`: CDASH types.
- `tokens.css`, `responsive.css`: design tokens (including viewport-relative drawer width tiers `--drawer-width-sm` [440px], `--drawer-width-md` [480px], `--drawer-width-lg` [640px], `--drawer-width-xl` [920px] and semantic mappings `--semantic-drawer-width-*`, and the AI output colour `--semantic-color-ai`, `--semantic-color-ai-bg` and `--semantic-color-ai-border` reserved for agent output, UX-8 and ADR-2635) and responsive utilities (`.drawer-mobile-full` and `.drawer-panel-mobile-full` for <=640px viewports) (exported as `ui/tokens.css`, `ui/responsive.css`).
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
