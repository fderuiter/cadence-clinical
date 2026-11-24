# ADR-2550: Co-Host Interactive Demos on GitHub Pages Subpaths

- **Status:** Accepted
- **Date:** 2026-11-20
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Supersedes:** [ADR-071](2026-08-14-github-pages-documentation-hosting.md)
- **Requirements:** PRD-SYS-001 (Operational boundary isolation and public evaluation portal integrity)

---

## 1. Context & Problem Statement

Under [ADR-071](2026-08-14-github-pages-documentation-hosting.md), GitHub Pages published only the static VitePress documentation portal (`docs/.vitepress/dist`) at `/cadence-clinical/`, and `scripts/build-docs.js` overwrote `docs/index.md` with the maintainer repository `README.md`. While [ADR-2546](2026-11-19-isolate-static-demo-builds-with-stateful-msw-handlers.md) isolated static demo builds for both the staff workspace (`apps/web`) and the participant portal (`apps/subject-portal`) behind stateful, fail-closed Mock Service Worker (MSW) handlers, neither demo bundle was built or published by `.github/workflows/deploy-docs.yml`.

As a result:

1. Evaluators visiting `/cadence-clinical/` landed on developer clone and container instructions rather than a guided overview with one-click demo launch links.
2. Evaluators without `.NET 10`, PostgreSQL, Docker, and Keycloak could not explore the staff workbench or participant eCOA/eConsent workflows in a browser.
3. Hardcoded `--base` CLI flags in `apps/web/package.json` and `apps/subject-portal/package.json` (plus `createWebHistory` in demo builds) prevented clean co-hosting and deep-link refreshes on static GitHub Pages subpaths.

## 2. Decision Drivers & Constraints

- **Zero-backend public evaluation:** Visitors must launch both the staff demo (`/cadence-clinical/demo/`) and the participant demo (`/cadence-clinical/subject-portal/`) directly from the portal home page and top navigation with synthetic data and zero external API or IdP dependencies.
- **Maintainer overview preservation:** The root `README.md` must remain accessible inside the documentation site as a dedicated page (`/cadence-clinical/README.html`) rather than being lost when `docs/index.md` becomes an authored VitePress home page.
- **Static deep-link resilience without breaking OIDC:** Demo builds must survive browser refreshes on deep links without server-side URL rewrites, while standard and production builds retain `createWebHistory` for Keycloak authorization-code + PKCE callbacks ([ADR-2493](2026-09-30-keycloak-only-web-sign-in.md)).
- **Single-worker subpath isolation & artifact provenance:** Each subpath must be owned by a single service worker scoped to its base path, and the staged demo bundles must be included inside `docs/.vitepress/dist` before SHA256 checksums (`SHA256SUMS`) and SLSA provenance (`artifact.tar`) are generated.

## 3. Options Considered

### Option 1: Co-Hosted Subpath Bundles Inside the Unified Pages Artifact with Demo Hash Routing (Selected)

- **Overview:**
  - Author a guided VitePress landing page at `docs/index.md` and stage the root `README.md` to `docs/README.md` during documentation builds.
  - Parameterize bundle base paths via `VITE_BASE_PATH` in `apps/web` (built under `/cadence-clinical/demo/`) and `apps/subject-portal` (built under `/cadence-clinical/subject-portal/`), removing hardcoded `--base` CLI overrides from package scripts.
  - Switch Vue Router to `createWebHashHistory(base)` exclusively when `isDemoBuild()` is true, preserving `createWebHistory(base)` for all non-demo builds.
  - Stage `apps/web/dist` into `docs/.vitepress/dist/demo` and `apps/subject-portal/dist` into `docs/.vitepress/dist/subject-portal` via `scripts/stage-portal-demos.js` in `.github/workflows/deploy-docs.yml` prior to checksum and SLSA provenance generation.
- _**Pros:**_ Single atomic GitHub Pages deployment covered by one `SHA256SUMS` manifest and SLSA attestation; clean service-worker scope separation (`/cadence-clinical/demo/` vs. `/cadence-clinical/subject-portal/`); zero 404s on deep-link reload; zero impact on production OIDC routing.
- _**Cons:**_ Slightly longer Pages build job due to compiling two additional Vite bundles alongside VitePress.

### Option 2: Documentation-Only GitHub Pages (`ADR-071` Status Quo)

- **Overview:** Keep GitHub Pages restricted to VitePress markdown output and require local `pnpm dev --mode demo` execution for interactive evaluation.
- _**Pros:**_ Minimal CI build duration.
- _**Cons:**_ Blocks non-developer evaluators and clinical stakeholders from inspecting interactive workflows without cloning the monorepo and installing Node.js/pnpm.

### Option 3: Separate Repositories or External Preview Hosting for Demos

- **Overview:** Publish demo SPAs to separate static hosting environments outside GitHub Pages.
- _**Pros:**_ Independent deployment lifecycles.
- _**Cons:**_ Splits provenance attestation, introduces cross-origin navigation drift between docs and demos, and adds external hosting credentials and operational overhead.

## 4. Decision Outcome

- **Chosen Option:** Option 1 (Co-Hosted Subpath Bundles Inside the Unified Pages Artifact with Demo Hash Routing).
- **Justification:** Co-hosting the VitePress documentation portal at `/cadence-clinical/` with isolated demo bundles under `/cadence-clinical/demo/` and `/cadence-clinical/subject-portal/` delivers immediate browser evaluation from one attested artifact while preserving strict Keycloak-only authentication and `createWebHistory` in production builds.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - Evaluators can launch both staff and participant experiences in one click from the portal home page and top navigation bar.
  - Deep links such as `/cadence-clinical/demo/#/studies/CADENCE-101` and `/cadence-clinical/subject-portal/#/wallet` reload cleanly on GitHub Pages without server rewrite rules.
  - Both staged demo directories are cryptographically hashed in `SHA256SUMS` and attested by `actions/attest-build-provenance`.
- **Negative Impact & Mitigations:**
  - VitePress links targeting `/demo/` and `/subject-portal/` must declare `target: "_self"` so VitePress's client-side router performs a full browser navigation into the mounted SPA rather than rendering VitePress's 404 component, and `assertThemeLinksExist` explicitly whitelists those two co-hosted subpaths while enforcing page existence for all documentation routes.

## 6. Implementation & Verification

- **Affected Files:**
  - `docs/index.md`, `scripts/build-docs.js`, `.gitignore`, `packages/tooling_core/src/Services/DocIndexSyncService.cs`
  - `docs/.vitepress/config.mjs`, `docs/.vitepress/repoLinks.mjs`
  - `scripts/stage-portal-demos.js`, `package.json`, `.github/workflows/deploy-docs.yml`
  - `apps/web/package.json`, `apps/web/src/router/index.js`, `apps/web/src/main.js`
  - `apps/subject-portal/package.json`, `apps/subject-portal/vite.config.js`, `apps/subject-portal/index.html`, `apps/subject-portal/public/manifest.json`, `apps/subject-portal/public/sw.js`, `apps/subject-portal/src/router/index.js`
  - `README.md`, `docs/LOCAL_DEV_ENVIRONMENT.md`, `docs/ENVIRONMENT_VARIABLES.md`, `apps/web/README.md`, `apps/subject-portal/README.md`
- **Verification:**
  - Unit tests verify hash vs. web history selection, subpath MSW service-worker resolution, subpath service-worker shell asset paths, and VitePress theme link validation.
  - End-to-end static server verification confirms `/cadence-clinical/`, `/cadence-clinical/README.html`, `/cadence-clinical/demo/`, and `/cadence-clinical/subject-portal/` load with zero failed asset requests and survive deep-link reloads.
