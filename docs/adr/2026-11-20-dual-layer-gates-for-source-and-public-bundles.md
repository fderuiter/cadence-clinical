# ADR-2497: Dual-Layer Gates for Source and Public Bundles

- **Status:** Accepted
- **Date:** 2026-11-20
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001 (de-identification and security compliance gates), HIPAA Safe Harbor (45 CFR §164.514(b)(2))
- **Extends:** ADR-2386, ADR-2532, ADR-2549, ADR-2550

---

## 1. Context & Problem Statement

Cadence Clinical publishes three static web artifacts to GitHub Pages (ADR-2550): the VitePress developer and regulatory documentation portal (`docs/.vitepress/dist`), the co-hosted Mock Service Worker (MSW) Staff Workbench demo (`apps/web/dist`, staged at `/demo/`), and the co-hosted Participant Portal demo (`apps/subject-portal/dist`, staged at `/subject-portal/`). Previously, compliance and security scanning left correlated blind spots across both source and built outputs:

1. **Source DEID exclusions (`DeidComplianceService`):** `docs/`, `stores/`, and staged root portal documents (`README.md`, `ARCHITECTURE.md`, `AGENTS.md`, `CONTEXT.md`, `CONTRIBUTING.md`, `SECURITY.md`, `SUPPORT.md`, `LICENSE`) were excluded from `check deid` (tracked in ADR-2386), allowing unreviewed emails, synthetic-looking national identifiers, and third-party media URLs (`images.unsplash.com`, `img.shields.io`) to enter published portal inputs.
2. **Source secret scanner exclusions (`SecretScanner`) and baseline blind spots:** `.vue` templates and `scripts/` (including `scripts/build-docs.js`) were excluded from `SecretScanner`, `.secrets.baseline` excluded `dist/`, and `scripts/build-docs.js` mutated `process.env` with compliance signing keys (`AUDIT_LOG_SECRET_KEY`, `INBOUND_EMAIL_HMAC_SECRET`, `GATEWAY_SECRET_KEY`) across the entire VitePress compilation process.
3. **Unverified public build outputs and missing browser CSP:** Comments such as `deid-ignore` or `pragma: allowlist` apply to source lines, not compiled bundles, and Vite/VitePress builds previously lacked a post-build verifier or same-origin `Content-Security-Policy` meta directives before SLSA provenance attestation and GitHub Pages artifact upload.

## 2. Decision Drivers & Constraints

- **Zero PHI/PII or secret leakage in public Pages artifacts:** Every file shipped in `docs/.vitepress/dist` (including `/demo/` and `/subject-portal/`), `apps/web/dist`, and `apps/subject-portal/dist` must be verified after compilation with zero comment exemptions.
- **Snapshot integrity (ADR-2532):** Vendored standards (`docs/CDISC/`, `docs/NCI/`) and GxP qualification runs and snapshots (`docs/SDLC/runs/`, `Requirements_Traceability_Matrix.md`, `IQ_OQ_PQ_Execution_Report.md`) must remain untouched on feature branches.
- **Least-privilege build environments:** Compliance traceability secrets must never be visible to VitePress or frontend asset bundlers.
- **Offline/same-origin portal execution:** Static portal and demo pages must enforce a strict `Content-Security-Policy` without breaking VitePress local search or same-origin MSW service workers.

## 3. Options Considered

### Option 1: Source-Only Scanning

- **Overview:** Expand `DeidComplianceService` and `SecretScanner` over source files only, relying on pre-commit and source CI gates.
- **Pros:**
  - ✅ Fast execution with no build prerequisite.
- **Cons:**
  - ❌ Cannot catch build-time environment interpolation, bundler plugin injections, `deid-ignore` pragmas hiding data shipped to public HTML/JS, or missing CSP headers in compiled HTML.

### Option 2: Post-Build Bundle Scanning Only

- **Overview:** Leave source scanners unchanged and scan only `docs/.vitepress/dist`, `apps/web/dist`, and `apps/subject-portal/dist` in CI.
- **Pros:**
  - ✅ Inspects exact published bytes.
- **Cons:**
  - ❌ Shifts feedback late in CI rather than catching violations in `cadence check --pre-commit` and `dotnet-quality`.

### Option 3: Dual-Layer Source and Zero-Exemption Public Bundle Gates (Selected)

- **Overview:** Harden Layer 1 source gates (`deid`, `security`, `clean-secrets`), isolate `scripts/build-docs.js` and `scripts/stage-portal-demos.js` child-process environments, inject same-origin CSP meta headers into VitePress, staff demo, and participant demo entrypoints, and enforce a Layer 2 zero-exemption `public-bundle` quality gate before artifact upload and SLSA attestation.
- **Pros:**
  - ✅ Immediate source feedback on authored `docs/`, root docs, `.vue`, `stores/`, and `scripts/` while protecting vendored standards and GxP snapshots.
  - ✅ Zero-exemption verification of exact compiled `docs/.vitepress/dist`, `apps/web/dist`, and `apps/subject-portal/dist` outputs before Pages publication or provenance signing.
  - ✅ Browser-enforced defense in depth via same-origin `Content-Security-Policy` headers with SHA-256 hashes for VitePress bootstrap scripts.
- **Cons:**
  - ❌ `public-bundle` requires compiled bundles and therefore runs in `docs-build` and `deploy-docs.yml` (`PreCommitMode.Skipped` in pre-commit).

## 4. Decision Outcome

- **Chosen Option:** Option 3 — Dual-Layer Source and Zero-Exemption Public Bundle Gates.
- **Justification:** Combining source-level guardrails with a fail-closed, zero-exemption post-build verifier and browser CSP headers eliminates single-point bypasses across authoring, build-time environment handling, and GitHub Pages publication.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - Authored `docs/`, root portal Markdown/license files, `.vue` components, and `stores/` are continuously scanned by `check deid` with strict synthetic email domain validation (`@cadence.clinical`, `@cadence-clinical.com`, `@ccrsoft.com`, `@cadence.example`, `.invalid`, plus the nine canonical `ADR-2549` demo persona `@example.com` addresses), allowing `.deid-baseline.txt` to drop all `ssn_national_id`, `urls`, and `email` entries.
  - `SecretScanner` inspects `.vue` and `scripts/` for private keys, AWS/GCP/Azure credentials, GitHub/Slack/Stripe tokens, and hardcoded compliance secrets, and `.secrets.baseline` no longer excludes `dist/`.
  - `scripts/build-docs.js` scopes ephemeral signing keys strictly to its pre-build CLI verification subprocesses (`check openapi`, `check schema-doc`, `check markdown`, `check rtm`) without mutating `process.env` or setting `RTM_DRAFT`, and passes a minimal non-secret variable allowlist to `vitepress build docs` and `scripts/stage-portal-demos.js`.
  - `PublicBundleValidatorService` (`check public-bundle` / `validate-public-bundle`) blocks publication if national IDs, unapproved emails, secrets, external assets, executable localhost URLs, or missing/permissive CSP headers appear in built outputs across all three bundles and staged subpaths.
- **Negative Impact / Technical Debt:**
  - `check public-bundle` requires `pnpm --filter ui build`, `pnpm docs:build`, and `pnpm docs:stage-demos` to have run first.
- **Mitigation Strategy:**
  - `public-bundle` is marked `PreCommitMode.Skipped` in `.githooks/pre-commit`, excluded in `dotnet-quality`, and executed deterministically in `ci.yml` (`docs-build`) and `deploy-docs.yml` prior to checksum generation, `actions/upload-pages-artifact`, and `actions/attest-build-provenance`.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/tooling_core`, `packages/cli`, `scripts/build-docs.js`, `scripts/stage-portal-demos.js`, `docs/.vitepress/config.mjs`, `apps/web`, `apps/subject-portal`, `.github/workflows/ci.yml`, `.github/workflows/deploy-docs.yml`.
- **Verification Plan:** Unit suites in `Cadence.UnitTests` (`DeidComplianceServiceTests`, `SecretScannerTests`, `PublicBundleValidatorServiceTests`, `QualityGateRegistryTests`, `LocalCiPlanTests`), PostgreSQL 18 integration and browser-to-database verification (`Cadence.IntegrationTests`, `Cadence.E2ETests`), frontend Vitest and Playwright E2E accessibility suites (`subject-portal` and `web --project=accessibility`), Native AOT CLI publish, full `pnpm docs:build` + `pnpm docs:stage-demos` + `cadence check public-bundle`, and headless Chromium CSP verification across all co-hosted portal routes.
