# ADR-2822: Unify CTMS, eTMF, and Study Training Contracts and Enforce Vue Template Typechecks

- **Status:** Accepted
- **Date:** 2026-11-22
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CTMS-001 (Startup Greenlight Readiness, DoA Log, and Monitoring Visit Letters), PRD-ETMF-001 (Scoped eISF Upload, Technical QC Staging, and Automated TMF 05.02.06 Filing), PRD-TRN-001 (Study Curricula, Modules, and Learner Part 11 Completion), PRD-SYS-001 (21 CFR Part 11 Step-Up Signatures, RBAC, and Audit Attribution)

---

## 1. Context & Problem Statement

Following the initial wiring of the CTMS, eTMF, and Study Training workspaces (ADR-2642, ADR-2643), several contract mismatches remained between the backend minimal APIs (`apps/ctms`, `apps/etmf`, and `apps/knowledge`) and the Vue 3 workspaces (`apps/web`):

1. **CTMS Startup Greenlight:** `CtmsGreenlightSection.vue` expects structured `facets`, `blocking_reasons`, `is_ready_for_certification`, `certified_by`, `certified_at`, `evaluated_at`, and `total_certified` fields alongside site initialization/update routes (`POST`/`PUT /api/v1/ctms/startup/sites/{siteId}/greenlight`) and `CADENCE-101` baseline readiness records (`SITE-101` and `SITE-102`).
2. **CTMS Delegation of Authority & Monitoring Letters:** `DoaEndpoints` blocked initial task delegation on new sites with a prior-row `SiteExistsAsync` 404 gate and omitted derived `status` (`REVOKED`, `ACTIVE`, `PENDING_PI_SIGNATURE`), `MonitoringVisitEndpoints` omitted persisted `findings` and preview `subject`/`body_markdown` aliases, and `LetterTemplateEndpoints` did not persist `StudyId`/`SiteId` on `GeneratedVisitLetter` or publish signed PDFs to eTMF artifact `05.02.06` (`Monitoring Visit Correspondence`).
3. **eTMF Study/Site Scope & QC Staging:** `DocumentManagerView.vue` and `DocumentGrid.vue` dropped `siteId` during single/batch uploads and document refreshes, and newly uploaded `DRAFT` documents lacked an audited transition (`POST /api/v1/etmf/documents/{document_id}/stage-qc`) into `TECHNICAL_QC` before QC approval.
4. **Study Training Curricula, Modules & Learner Completion:** `TrainingEndpoints` lacked curriculum creation (`POST /api/v1/training/curricula`), persisted `TrainingCurriculumModule` records, nested `curriculum` details on assignments, self-service learner Part 11 completion (`POST /api/v1/training/assignments/{assignment_id}/complete`), and `CADENCE-101` seed records.
5. **Frontend Template Drift:** `apps/web` typechecking (`pnpm --filter web typecheck` and `ApiClientValidatorService`) ran `tsc` only on `.ts` files rather than `vue-tsc --noEmit`, allowing `.vue` template/script contract mismatches against generated OpenAPI schemas to slip past CI.

## 2. Decision Drivers & Constraints

- **End-to-End OpenAPI Contract Fidelity:** Backend C# DTOs, exported OpenAPI specifications (`docs/openapi`), generated TypeScript clients (`apps/web/src/api`), and `.vue` templates must agree on every property name, nullability rule, and HTTP verb.
- **21 CFR Part 11, RBAC & Scope Isolation (PRD-SYS-001):** Every state-mutating route must enforce `PermissionMatrix` capabilities, study/site scope guards (`SiteScopeGuard` / `StudyScopeGuard`), `X-Change-Reason` attribution, `IClock` timestamps, and single-use durable `X-Sig-Token` verification on electronic signatures.
- **Event-Driven Cross-Service Filing (ADR-2814):** Signed CTMS monitoring visit letters must file into eTMF asynchronously via MassTransit (`EtmfDocumentFilingRequested`) rather than coupling CTMS directly to eTMF persistence.

## 3. Options Considered

### Option 1: Unify C#/OpenAPI Contracts, Publish MassTransit eTMF Filing Events, and Enforce `vue-tsc --noEmit` in CI (Selected)

- **Overview:** Extend CTMS (`StartupGreenlightEndpoints`, `DoaEndpoints`, `MonitoringVisitEndpoints`, `LetterTemplateEndpoints`, `CtmsStartupSeeder`, and `PermissionMatrix.Ctms`), eTMF (`EtmfIntelligenceEndpoints` `site_id` filtering and `stage-qc` transition plus `EtmfDocumentFilingRequestedConsumer`), and Knowledge (`TrainingEndpoints`, `TrainingCurriculumModule` migration, `KnowledgeCatalogSeeder`, and `PermissionMatrix.Knowledge`); regenerate OpenAPI specs, API docs, schema docs, and web TypeScript clients; and switch `apps/web` typechecking and `ApiClientValidatorService` to `vue-tsc --noEmit`.
- **Pros:**
  - ✅ Aligns every CTMS, eTMF, and Study Training workflow end-to-end against real persisted contracts with zero fabricated client state.
  - ✅ Enforces compile-time OpenAPI type safety across all `.vue` templates and `<script setup lang="ts">` blocks in `apps/web` and `cadence check api-clients`.
- **Cons:**
  - ❌ Requires EF Core schema migrations in `ctms` and `knowledge` plus fixing pre-existing `.vue` template type mismatches surfaced by `vue-tsc`.

### Option 2: Add Ad-Hoc Frontend Adapter Shims Without Updating OpenAPI or Enabling `vue-tsc`

- **Overview:** Patch property aliases only inside `apps/web` components and leave missing routes (`stage-qc`, curriculum creation, learner completion, greenlight `POST`/`PUT`) unbacked.
- **Pros:**
  - ✅ Avoids database migrations.
- **Cons:**
  - ❌ Leaves critical site-startup, eISF QC, DoA bootstrap, letter filing, and learner completion journeys incomplete and allows future `.vue` template drift.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Unifying the C# endpoints, EF Core models, MassTransit `EtmfDocumentFilingRequested` contract, OpenAPI schemas, and `vue-tsc --noEmit` gate eliminates contract drift at the source and guarantees CI catches any future `.vue` template mismatch.

## 5. Consequences & Trade-offs

- **Positive Impact:** Full startup greenlight initialization/certification, first-row DoA delegation, monitoring findings and `05.02.06` TMF auto-filing, site-scoped eISF upload and `TECHNICAL_QC` staging, curriculum authoring and learner Part 11 completion, deterministic `CADENCE-101` startup seeding (gated on `Database:MigrateOnStartup` / `Database:SeedOnStartup`), and full `vue-tsc` coverage.
- **Negative Impact / Technical Debt:** `vue-tsc --noEmit` slightly increases `pnpm --filter web typecheck` execution time compared to plain `tsc`.
- **Mitigation Strategy:** Scoped `tsconfig.json` includes (`src/**/*.ts`, `src/**/*.vue`) and shared `SlotsType` declarations in `packages/ui/index.d.ts` keep `vue-tsc` fast and deterministic.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/core-models`, `packages/security`, `packages/tooling_core`, `packages/ui`, `apps/ctms`, `apps/etmf`, `apps/knowledge`, `apps/web`, `docs/openapi`, `docs/api`, `docs/schema`.
- **Verification Plan:** Verified via `dotnet build Cadence.sln`, `Cadence.ArchitectureTests`, `Cadence.UnitTests`, `pnpm --filter web typecheck` (`vue-tsc --noEmit`), `pnpm -r lint`, `pnpm -r test`, `pnpm -r build`, and `Cadence.Cli check`.
