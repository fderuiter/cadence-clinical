# ADR-2498: Hoist CSP Meta Directives, Delegate Clinical Widgets, and Scope Mode-Aware Vite CSP Profiles

- **Status:** Accepted
- **Date:** 2026-11-22
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001 (de-identification and security compliance gates), WCAG 2.2 Level AA
- **Extends:** ADR-2497, ADR-2550

---

## 1. Context & Problem Statement

ADR-2497 introduced browser `Content-Security-Policy` `<meta>` tags across the three static GitHub Pages surfaces (`docs/.vitepress/dist`, `apps/web/dist` staged at `/demo/`, and `apps/subject-portal/dist` staged at `/subject-portal/`) along with post-build checks in `PublicBundleValidatorService`. End-to-end Chromium runtime auditing identified four structural CSP gaps:

1. **Late CSP `<meta>` placement in `<head>`:** VitePress appends `config.head` entries after its generated `<link rel="preload">`, stylesheet, module entry `<script>`, and inline dark-mode/page-data `<script>` tags, and Vite injects production `<script type="module">` and `<link rel="modulepreload">` tags near the top of `<head>`. Per the W3C CSP3 specification, `<meta http-equiv="Content-Security-Policy">` governs only resources fetched or executed _after_ the `<meta>` element is parsed, leaving initial scripts unguarded and failing to match inline script hashes that preceded the tag.
2. **Inline DOM event attributes in participant widgets:** `apps/subject-portal/src/widgets/clinical-fields.js` emitted inline `oninput` attributes on `createClinicalSlider` (`#pain_scale`) and inline `onclick` attributes on `createQueryPanel` (`.query-flag` and `.btn-close-panel`). Under `script-src 'self'` without `'unsafe-inline'`, Chromium blocks inline event attributes with `securitypolicyviolation` events.
3. **Hardcoded static CSP profiles in Vite entrypoints:** Hardcoding `connect-src 'self'` in `apps/web/index.html`, `apps/web/public/silent-check-sso.html`, and `apps/subject-portal/index.html` blocked Keycloak OIDC discovery and silent SSO iframe flows in non-demo builds, while also omitting `media-src 'self' blob:`, `worker-src 'self' blob:`, and `frame-src 'self' blob:` needed by `hls.js` adaptive streaming (`MediaSource` and Web Worker `blob:` URLs) and PDF previews (`MediaPlayer.vue`, which previously passed `data:` URIs to `<iframe data-testid="pdf-iframe">`).
4. **Incomplete post-build HTML checks in `PublicBundleValidatorService`:** `ValidateHtmlCsp` verified that a CSP `<meta>` existed with `script-src`, `connect-src`, `media-src`, and `worker-src`, but did not fail when `<script>` or `<link>` preceded the `<meta>` tag, when inline `on*=` attributes appeared in rendered HTML, or when `script-src` permitted `blob:`/`data:`/`'unsafe-hashes'` or `frame-src` permitted `data:`.

## 2. Decision Drivers & Constraints

- **Immediate CSP enforcement from document start:** Every compiled HTML page must emit `<meta http-equiv="Content-Security-Policy">` before the first `<script>` or `<link>` element in `<head>`.
- **Zero `'unsafe-inline'` or `'unsafe-hashes'` in `script-src`:** Interactive clinical sliders and query panels must work strictly through delegated JavaScript listeners while satisfying WCAG 2.2 Level AA (`48px` minimum touch targets, `aria-expanded` state management, `>= 3:1` control border contrast, and semantic tokens).
- **Zero cross-origin or local-dev host leakage in public demo bundles:** Demo bundles staged to GitHub Pages (`--mode demo` / `VITE_DEMO_MODE=true`) must emit zero local loopback, wildcard, or external host tokens, whereas non-demo development and production builds must permit the configured Keycloak origin (`VITE_KEYCLOAK_URL`) in `connect-src` and `frame-src`.
- **Safe media and PDF rendering:** HLS playback (`hls.js`) and PDF previews (`MediaPlayer.vue`) must operate via `blob:` object URLs (`media-src 'self' blob:`, `worker-src 'self' blob:`, `frame-src 'self' blob:`) with deterministic `URL.revokeObjectURL` cleanup and never permit `data:` in `frame-src` or `script-src`.

## 3. Options Considered

### Option 1: Add `'unsafe-hashes'` for Inline Handlers and `data:` to `frame-src`

- **Overview:** Hash existing `oninput`/`onclick` strings with `'unsafe-hashes'` in `script-src` and allow `frame-src 'self' data:`.
- **Pros:**
  - ✅ Minimal widget and component changes.
- **Cons:**
  - ❌ Weakens XSS containment: `data:` frames execute in opaque origins that can embed arbitrary HTML/scripts, and dynamic field keys (`#pain_scale_display`, `#query-panel-${key}`) cannot share a static `'unsafe-hashes'` allowlist.

### Option 2: External Reverse-Proxy Headers Only

- **Overview:** Remove `<meta http-equiv="Content-Security-Policy">` tags from HTML bundles and rely on custom HTTP response headers at deployment edges.
- **Pros:**
  - ✅ Avoids build-time HTML transforms.
- **Cons:**
  - ❌ GitHub Pages does not support custom `Content-Security-Policy` HTTP response headers, leaving all co-hosted docs and demo portals unprotected.

### Option 3: Hoisted CSP `<meta>` Injection, Delegated Clinical Widgets, `blob:` Media/PDF Previews, and Hardened `public-bundle` Gate (Selected)

- **Overview:** Hoist CSP `<meta>` tags immediately after `<head>` in VitePress `transformHtml` and Vite `transformIndexHtml` (`order: "post"`), replace inline widget handlers with `WeakMap`-guarded delegated listeners (`bindDelegatedClinicalWidgets`), convert `MediaPlayer.vue` PDF sources to `blob:` URLs, generate mode-aware CSP profiles in Vite (`demo` vs non-demo OIDC), harden `PublicBundleValidatorService`, and fail Playwright accessibility suites on any browser `securitypolicyviolation` event.
- **Pros:**
  - ✅ Browser enforces CSP across every `<script>` and `<link>` tag on all VitePress and Vite HTML documents, including `silent-check-sso.html`.
  - ✅ Preserves `script-src 'self'` (plus SHA-256 bootstrap hashes on VitePress only) with zero inline event handlers and zero `data:` frames.
  - ✅ Supports both hermetically isolated GitHub Pages demos and OIDC-enabled non-demo deployments from one deterministic build pipeline.
- **Cons:**
  - ❌ Requires post-render HTML transforms in VitePress and Vite and explicit `URL.revokeObjectURL` lifecycle cleanup in `MediaPlayer.vue`.

## 4. Decision Outcome

- **Chosen Option:** Option 3 — Hoisted CSP `<meta>` Injection, Delegated Clinical Widgets, `blob:` Media/PDF Previews, and Hardened `public-bundle` Gate.
- **Justification:** Hoisting `<meta http-equiv="Content-Security-Policy">` to the start of `<head>`, eliminating inline event handlers and `data:` iframes, scoping OIDC origins strictly to non-demo modes, and enforcing both static (`check public-bundle`) and runtime (`securitypolicyviolation` listeners in Playwright) gates closes every identified CSP gap without custom edge headers.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - `docs/.vitepress/config.mjs` (`injectHashedPortalCsp`) strips placeholder CSP tags, computes SHA-256 hashes over inline `<script>` elements, adds `frame-src 'self'`, and inserts the `<meta>` tag immediately after `<head>` before all `<script>` and `<link>` tags across every generated documentation page.
  - `apps/subject-portal/src/widgets/clinical-fields.js` preserves canonical `#${id}_display`, `#query-flag-${fieldId}` (`aria-controls="query-panel-${fieldId}"`), and `.btn-close-panel[data-field-id]` selectors with zero `on*=` attributes, and `bindDelegatedClinicalWidgets` binds idempotent delegated `input`, `change`, and `click` listeners for `QuestionnaireView` and `ConsentView` with WCAG 2.2 AA `48px` touch targets.
  - `packages/ui/src/components/MediaPlayer.vue` converts `data:application/pdf` inputs and fetched PDF streams into `blob:` URLs (`URL.createObjectURL`) and revokes owned object URLs on source updates and unmount; `apps/web/src/components/fileshare/MediaPreviewModal.vue` renders HLS and PDF previews through `MediaPlayer`.
  - `apps/web/vite.config.js` and `apps/subject-portal/vite.config.js` emit hoisted mode-aware CSP `<meta>` tags (including `silent-check-sso.html`), stripping OIDC origins in `demo` mode while allowing `VITE_KEYCLOAK_URL` origins in `connect-src` and `frame-src` for non-demo builds.
  - `PublicBundleValidatorService` rejects `<script>` or `<link>` tags before CSP `<meta>`, inline `on*=` HTML attributes outside code/script/style blocks, `'unsafe-hashes'`/`blob:`/`data:` in `script-src`, and `data:` in `frame-src`, while Playwright E2E suites fail on any browser `securitypolicyviolation` event.
- **Negative Impact / Technical Debt:**
  - New interactive HTML builders in `apps/subject-portal/src/widgets/` must use delegated event listeners rather than inline `on*=` attributes.
- **Mitigation Strategy:**
  - Both `PublicBundleValidatorServiceTests` (`check public-bundle`) and browser `securitypolicyviolation` listeners in Playwright catch any inline handler or unhoisted CSP regression automatically.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `docs/.vitepress/config.mjs`, `apps/subject-portal`, `apps/web`, `packages/ui`, `packages/tooling_core`, `tests/Cadence.UnitTests`.
- **Verification Plan:** xUnit tests in `PublicBundleValidatorServiceTests`, Vitest unit suites in `docs/.vitepress/navigation.test.mjs`, `packages/ui`, `apps/subject-portal`, and `apps/web`, full `pnpm docs:build` + `pnpm docs:stage-demos` + `cadence check public-bundle`, and Playwright Chromium accessibility + `securitypolicyviolation` verification across VitePress docs, `/subject-portal/`, and `/demo/` HLS/PDF previews.
