# ADR-071: GitHub Pages Documentation Hosting

- **Status:** Accepted
- **Date:** 2026-08-14
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Previously, GitHub Pages deployment was configured to build and publish the interactive frontend web application demo SPA (`apps/web/dist`). However, developer and compliance teams required a centralized, accessible web portal for architectural specifications, CDISC USDM data lifecycle schemas, Part 11 SDLC compliance artifacts, and developer guides.

Hosting only the VitePress developer documentation portal directly at the repository root on GitHub Pages (`https://<owner>.github.io/cadence-clinical/`) provides seamless, immediate access to all platform technical and regulatory documentation without embedding or mixing frontend application runtimes into the static Pages deployment.

This decision fulfills requirements under Trace-8.

## 2. Decision Drivers & Constraints

- **Driver 1:** Deploy the standalone VitePress developer documentation portal directly at root (`/cadence-clinical/`).
- **Driver 2:** Streamline the GitHub Actions deployment workflow (`.github/workflows/deploy-docs.yml`) to execute necessary pre-build validators (ADR check, Markdown validation, schema visualizer generation, draft RTM generation) before static compilation.
- **Driver 3:** Decouple GitHub Pages static asset hosting from the internal interactive web demo, isolating `apps/web` for local development workflows and unit test execution.
- **Driver 4:** Maintain full compliance with automated GxP validation pipelines and quality gates.

## 3. Options Considered

### Option 1: Dedicated VitePress Documentation Deployment at Repository Root (Selected)

Deploy exclusively the static output of VitePress (`docs/.vitepress/dist`) to GitHub Pages via `.github/workflows/deploy-docs.yml`.

- **Pros:** Fast build times, zero extraneous client bundle payload, direct URL resolution for architectural and SDLC documentation, and clean separation of concerns.
- **Cons:** Interactive web demo is no longer hosted on public GitHub Pages (available via local dev server `pnpm dev`).

### Option 2: Hybrid Nested Co-Hosting (`/` for Demo SPA, `/docs/` for VitePress)

Bundle the VitePress output into a nested subdirectory inside `apps/web/dist/docs`.

- **Pros:** Co-locates both demo and docs under a single Pages environment.
- **Cons:** Tight coupling between web app build and docs generation, increased build complexity, potential asset collision, and unneeded demo bundle overhead.

## 4. Decision Outcome

- **Chosen Option:** Option 1 (Dedicated VitePress Documentation Deployment at Repository Root)
- **Justification:** Option 1 provides direct, unobstructed access to the technical documentation and SDLC compliance guidelines under Trace-8 while simplifying CI/CD workflows and reducing deployment friction.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - Automated deployment of the documentation portal to GitHub Pages on every release/push to `main` via `deploy-docs.yml`.
  - Native client-side search and fast navigation for all ADRs, SDLC policies, and CLI documentation.
  - Simplified VitePress configuration (`base: "/cadence-clinical/"`, `outDir: "dist"`).
- **Negative Impact:**
  - The interactive web demo is accessed locally via `pnpm dev` rather than via public GitHub Pages.

## 6. Implementation & Verification

- **Affected Workflows & Files:**
  - `.github/workflows/deploy-docs.yml` (new documentation deployment workflow replacing the previous demo deployer)
  - `.github/workflows/ci.yml` (caller job updated to `deploy-docs`)
  - `docs/.vitepress/config.mjs` (configured `base` and `outDir`)
  - `docs/adr/2026-08-14-github-pages-documentation-hosting.md` (this record)
- **Verification Plan:**
  - Verified local build with `pnpm docs:build`.
  - Verified static output directory `docs/.vitepress/dist/index.html`.
  - Executed `uv run python scripts/validate_adrs.py` and `uv run python scripts/validate_markdown.py`.
