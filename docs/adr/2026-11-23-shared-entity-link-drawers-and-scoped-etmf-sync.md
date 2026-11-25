# ADR-2674: Shared EntityLink Peek Drawers and Scoped eTMF Workspace Synchronization

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001 (21 CFR Part 11 explicit audit justification, electronic signature inspection, and draft preservation), PRD-ETMF-001 (DIA TMF Reference Model binder navigation, site-scoped export confinement, and caller scope isolation), PRD-CRF-015 (WCAG 2.2 Level AA keyboard focus containment, plain-English clinical labels, and progressive disclosure)

---

## 1. Context & Problem Statement

Audits across `apps/etmf`, `apps/web`, and `packages/ui` identified four cross-workflow eTMF synchronization and entity-reference gaps:

1. **Stale Cross-Study / Cross-Site eTMF State and Dropped Scope Links:** `useEtmfStore.setScope` and `studyScopeBridge.ts` cleared error banners on study or site transitions without clearing cached `documentsList`, `qcQueue`, `activeDocument`, or `selectedDocumentIds` rows, and `DocumentManagerView.vue` could display cached rows from a previous scope on mount. In addition, raw `participant:<uuid>` and `enrollment:<uuid>` `NodeRef` values needed consistent normalization into human-readable `SubjectCode` query parameters without leaking raw graph tokens across workflow URLs.
2. **Study-Wide Binder Exports Under Active Site Scope:** `ExportDocumentsRequest` in `apps/etmf` accepted `study_id` and `document_ids` without an explicit `site_code` filter, causing `etmfStore.triggerExport` to package documents across all caller-authorized sites in a study even when an operator had narrowed the workspace to a single clinical site.
3. **Opaque Plain-Text eTMF References Across CTMS, Training, and Site Activation:** Compiled GxP monitoring visit letters (`CtmsMonitoringSection.vue`), bulk delegation sign-off manifests (`ProtocolTrainingConsole.vue`), and missing site activation documents (`SiteActivationPanel.vue`) rendered plain text (`Filed in eTMF`, `Zone 5 (05.02.04)`, raw missing document labels) with no in-place preview drawer or deep-link into `/etmf` or Clinical Canvas Documents (`/studies/:studyCode?stage=start&section=documents`).
4. **QC Inspection Friction, Unprotected Drafts, and Unmounted Client Helpers:** `CraQcQueue.vue` lacked an in-modal regulated PDF preview before 21 CFR Part 11 QC sign-off; unsaved upload, inline taxonomy, and QC drafts lacked composable dirty-navigation (`ADR-2671`) and sign-out draft guards; `PdfPreviewModal.vue`, `DocumentGrid.vue`, and `CraQcQueue.vue` exposed uppercase enum strings and header-level `DOC-*` identifiers; and `apps/web/src/api/etmf.ts` retained two unmounted legacy client helpers (`getCompleteness`, `signDocument`).

## 2. Decision Drivers & Constraints

- **Kernel Module Boundary Isolation (`K-XS-1`):** `Cadence.Apps.Etmf` (`TmfDocument` `DOC-*` primary keys and DIA taxonomy codes) must remain decoupled from Clinical Kernel `NodeKinds.cs` without cross-service database migrations.
- **Strict Caller Study & Site Scope (`PRD-ETMF-001`, `K-XS-1`):** Optional `site_code` (`SiteCode`, avoiding non-kernel `site_id` wire aliases per `ADR-2832`) on `POST /api/v1/etmf/documents/export` must enforce `ClinicalScopeGuard.CanAccessSite` (returning HTTP `403` `FORBIDDEN_SCOPE` for unauthorized sites) across synchronous ZIP streams, asynchronous `TmfExportJob` workers, and export-job status/download/list endpoints.
- **Non-Disruptive URL Deep-Linking & Composable Dirty Guards (`ADR-2651`, `ADR-2671`):** Synchronizing `documentId`, `artifactCode`, and `tab` (`binder` | `qc-queue`) in `/etmf` and `/studies/:studyCode?stage=start&section=documents` must use `router.replace` so in-place selection updates never trigger false unsaved-draft prompts while genuine route leaves, cross-study/site switches, and voluntary sign-out remain protected via `useUnsavedNavigationGuard` and `registerDraftSource`.
- **Cold-Boot Store Deferral & WCAG 2.2 AA Design System (`PRD-CRF-015`, `ADR-2675`, `ADR-2676`):** Shared `<EntityLink>` and `<EntityPeekDrawer>` primitives must live in `packages/ui` using semantic `tokens.css` custom properties, `:not(:disabled)` hover guards, `ClinicalDrawer` focus containment, and progressive `<details>` disclosure while preserving inline focus handoffs (`handoffFocus` / `useInlineFocusHandoff`) and cold-boot store deferral.

## 3. Options Considered

### Option 1: Client-Side Frontend Adapter (`entityLinkAdapter`) + Shared `EntityLink` / `EntityPeekDrawer` in `packages/ui` + Site-Scoped eTMF Export & Query Sync (Selected)

- **Overview:** Export `EntityLink`, `EntityPeekDrawer`, and pure `entityLinkAdapter` helpers (`resolveDiaArtifactMeta`, `resolveEntityLinkDescriptor`, `buildEntityBinderUrl`) from `packages/ui` to resolve `TmfDocument` records, DIA artifact codes, and activation document names into human-readable clinical labels and scoped `/etmf` deep-links without coupling `Cadence.Apps.Etmf` to `NodeKinds.cs`; reset cached eTMF state immediately on every study/site switch or session teardown; normalize `participant:<uuid>`, `enrollment:<uuid>`, and bare UUIDs to `SubjectCode` across route-scope helpers; add `site_code` scope enforcement and filtering to `ExportDocumentsRequest`, `TmfExportJob`, and `EtmfExportWorker`; synchronize `tab`, `artifactCode`, and `documentId` via `router.replace`; embed `PdfPreviewModal` in `CraQcQueue.vue`; wire `useUnsavedNavigationGuard` (`ADR-2671`) and `registerDraftSource` across eTMF upload, taxonomy, and QC drafts; format clinical status/enum labels while moving `DOC-*` system IDs into `<SupportDetails>`; and prune `getCompleteness` and `signDocument` from `apps/web/src/api/etmf.ts` while keeping demo `AuditView.vue` MSW flows and explicit `SignatureCaptureModal.vue` `onSign` callbacks intact.
- **Pros:**
  - Provides reusable, accessible entity chips and contextual peek drawers across CTMS, Training, Site Activation, and future modules while respecting `K-XS-1` module boundaries.
  - Eliminates cross-study/site data flashes, unscoped binder exports, raw `NodeRef` query leaks, and unmounted eTMF API helpers.
- **Cons:**
  - `TmfExportJob` persists site scope via structured `ExportReason` metadata (`TmfExportJob.FormatExportReason` / `TryParseSiteScope`) to avoid schema migrations on existing deployments.

### Option 2: Migrate `TmfDocument` Rows Into Kernel `Artifact` UUIDv7 Nodes

- **Overview:** Convert `TmfDocument` primary keys to Kernel `Artifact` UUIDv7 rows and register eTMF documents in `NodeKinds.cs` and `CanvasInspector.vue`.
- **Pros:**
  - Unifies `NodeRef` syntax across Kernel observations and eTMF documents.
- **Cons:**
  - Requires destructive cross-service database migrations and couples `Cadence.Apps.Etmf` to Kernel graph types, violating task scope and `K-XS-1` service isolation.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Option 1 delivers contextual regulated document previews, deep-linkable `/etmf` and Clinical Canvas Documents state, site-scoped binder export confinement, Part 11 QC PDF inspection, composable draft loss prevention, and plain-English clinical labels with zero cross-service schema coupling.

## 5. Consequences & Trade-offs

- **Positive Impact:** Operators switching studies or sites never see stale eTMF documents or QC items; binder exports respect active site scope on both client and server (`403 FORBIDDEN_SCOPE` when unauthorized); CTMS visit letters, PI delegation sign-off manifests, and missing site activation items open accessible `EntityPeekDrawer` previews that deep-link into `/etmf` with full scope intact; CRAs can preview stored PDFs directly inside QC review modals; unsaved eTMF drafts warn before route leave, cross-scope switches, or sign-out without firing on `router.replace` URL sync; and unmounted eTMF client helpers are eliminated.
- **Negative Impact / Technical Debt:** `packages/ui` exports two additional Vue components (`EntityLink`, `EntityPeekDrawer`) and adapter utilities (`entityLinkAdapter`) whose TypeScript declarations in `packages/ui/index.d.ts` must stay synchronized via `pnpm --filter ui build` alongside workspace source resolution (`ADR-2658`).
- **Mitigation Strategy:** Enforced via `packages/ui/tests/EntityLink.spec.ts`, `packages/ui/tests/design_system_harmony.test.js`, `tests/Cadence.UnitTests/Etmf/EtmfExportTests.cs`, `tests/Cadence.ArchitectureTests`, and Vitest suites across `apps/web/tests/`.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `apps/etmf`, `apps/web`, `tests/Cadence.UnitTests`, `docs/openapi/`, `docs/components/`, `docs/adr/`.
- **Verification Plan:** Verified via `pnpm --filter ui build`, `pnpm --filter ui test`, `dotnet test` (`Cadence.UnitTests` & `Cadence.ArchitectureTests`), `pnpm openapi:export`, `pnpm --filter web generate:api`, `pnpm -r typecheck`, `pnpm lint`, `pnpm test`, `pnpm build`, `pnpm build:demo`, headless Chromium screenshots, and `cadence check` gates.
