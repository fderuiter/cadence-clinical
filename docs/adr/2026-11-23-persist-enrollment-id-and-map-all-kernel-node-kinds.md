# ADR-2606: Complete Kernel NodeKinds Scope Mapping and Persist Canonical Enrollment NodeRefs

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SUB-001 (subject enrollment synchronization and canonical enrollment NodeRef resolution), PRD-CRF-013 (site and tenant data isolation across kernel scope zoom paths), PRD-SYS-192 (deterministic demo seeding across kernel and execution), kernel spec K-ID-6, K-ID-7, K-IDN-1

---

## 1. Context & Problem Statement

[ADR-2457](2026-09-30-grant-backed-node-visibility.md) and [ADR-2481](2026-09-30-public-noderef-and-kernel-computed-zoom-path.md) established public `NodeRef`s (`<kind>:<uuid>[@<version>]`) and kernel-computed zoom paths for the Clinical Canvas. However, two cross-service gaps remained across `Cadence.Kernel` and `Cadence.Apps.Execution`:

1. **Incomplete `KernelScopeGraph` coverage and missing-parent failures:** `NodeKinds.All` declared 38 canonical node kinds, while `KernelScopeGraph.FindAsync` mapped 22. Navigating, tracing, or resolving zoom paths for the remaining 16 kinds—including `person`, `participant`, `tenant_membership`, `grant`, `qualification`, `qualification_requirement`, `effectivity`, `subscription`, `product_entitlement`, `form_version_snapshot`, `study_standards_profile`, `validation_run`, `code_system`, `code_system_version`, `concept_mapping`, and `work_queue_definition`—returned `404` or masked study-scoped artifacts because `form_version_snapshot`, `study_standards_profile`, and `validation_run` were absent from `KernelAuthorityStore.PathBearingKinds`. In addition, `validation_finding` linked directly to `study` instead of parsing `FindingTarget` (`collection_instance`, `observation`, `enrollment`, `study_site`), and `KernelNavigator.GetZoomPathAsync` threw `InvalidOperationException` if an intermediate target row had not yet been projected.
2. **Ephemeral `EnrollmentId` in Execution and dual participant URLs in Clinical Canvas:** `HttpKernelEnrollmentSynchronizer` returned the kernel's `EnrollmentId` when screening a subject, but `ClinicalSubject` discarded it and `ExecutionDbSeeder` generated random `SubjectId`s that diverged from `DevelopmentDemoData`. Consequently, `apps/web` emitted `participant:<subjectUuid>` URLs while kernel lineage, activity feeds, eCRF collections, and queries emitted `enrollment:<enrollmentUuid>`, splitting inspector state, casebook links, and command discovery across two distinct identifiers.

## 2. Decision Drivers & Constraints

- **100% `NodeKinds.All` Resolution & Masked Read Contract (`K012`, `K027`):** Every kind in `NodeKinds.All` must resolve through `GET /api/v1/kernel/nodes/{nodeRef}` and `/path` for seeded records in the active tenant, returning `mask: "NotPermitted"` (`200 OK`) rather than `404` when a caller lacks tenant-level scope (e.g., site-scoped callers reading `qualification` or `subscription`).
- **Zero-PII Identity & Tenant Membership Boundary (`K-IDN-1`, `K003`):** `participant` and `person` nodes must expose only pseudonymous labels (`"Participant"`, `"Person"`, `"Active"`/`"Inactive"`, `Code: null`) with zero PII, and `person` resolution must require an active `TenantMembership` row in the caller's tenant (`404` otherwise).
- **Resilient Collection Finding Zoom Paths (`K022`):** Without altering `KernelDbContext` tables, `validation_finding` must derive its immediate parent from `ValidationDetectionRecord.TargetKind` and `TargetKey` (`DatasetRecord`/`collection_instance`, `observation`, `enrollment`, `study_site`) and fall back cleanly to `FallbackParent` (`study`) or `tenant` in `KernelNavigator.GetZoomPathAsync` when an intermediate target row is absent, while preserving cycle detection (`MaxDepth = 16`).
- **Canonical Enrollment `NodeRef`s (`K-ID-6`, `K-ID-7`):** Execution must persist `KernelEnrollmentId` on `ClinicalSubject`, return `enrollment_ref` (`enrollment:<uuid>`) on subject create, list, and detail responses, seed deterministic UUID v8 parity with `DevelopmentDemoData`, and allow Clinical Canvas to emit canonical `enrollment:<uuid>` links while retaining backward-compatible fallback support for `participant:<uuid>` URLs.

## 3. Options Considered

### Option 1: Complete `KernelScopeGraph` + Persist `ClinicalSubject.KernelEnrollmentId` + Canonical `enrollment:<uuid>` Canvas Links (Selected)

- **Overview:** Map all 38 `NodeKinds` in `KernelScopeGraph.FindAsync` (and batch 11 high-traffic kinds in `FindManyAsync`); enforce active `TenantMembership` checks and pseudonymous labels for `person` and `participant`; parent `qualification` and `subscription` under `tenant` (`product_entitlement` under `subscription`); link `grant`, `qualification_requirement`, and `effectivity` to their scoped `study_site`, `study` (with country group when applicable), `organization`, or `tenant` parents; register `form_version_snapshot`, `study_standards_profile`, and `validation_run` in `KernelAuthorityStore.PathBearingKinds`; parse `ValidationDetectionRecord` targets for `validation_finding` with `FallbackParent` support in `KernelNavigator.GetZoomPathAsync`; persist nullable `kernel_enrollment_id` on `execution.clinical_subjects` with deterministic demo seeding in `ExecutionDbSeeder`; expose `enrollment_ref` across Execution subject DTOs and OpenAPI schemas; and emit canonical `enrollment:<uuid>` references across `StudyCanvasView`, `CanvasInspector`, `CanvasPalette`, and `studyCanvas` store with `participant:<uuid>` URL fallback.
- **Pros:**
  - ✅ Reaches 38/38 `NodeKinds.All` scope-graph coverage without modifying `KernelDbContext` schema tables.
  - ✅ Enforces `K-IDN-1` zero-PII guarantees, cross-tenant `person` isolation, and `NotPermitted` masking for site-scoped callers.
  - ✅ Unifies Clinical Canvas selection, activity stream chips, command palette, eCRF forms, and lineage around `enrollment:<uuid>`.
- **Cons:**
  - ❌ Adds a nullable `kernel_enrollment_id` column and composite tenant index to `execution.clinical_subjects` for transitional compatibility with pre-kernel subject fixtures.

### Option 2: Resolve `EnrollmentId` Dynamically on Every Read Without Persisting on `ClinicalSubject`

- **Overview:** Leave `execution.clinical_subjects` unchanged and have `apps/web` or `Cadence.Apps.Execution` query `/api/v1/kernel/studies/{code}/enrollments/by-subject-code/{subjectCode}` on every subject list or inspector load.
- **Pros:**
  - ✅ Avoids an EF Core migration on `execution.clinical_subjects`.
- **Cons:**
  - ❌ Introduces `N+1` cross-module lookups on participant lists, drops the `EnrollmentId` already returned at write time by `HttpKernelEnrollmentSynchronizer`, and prevents direct `enrollment:<uuid>` lookup in `SubjectApplicationService`.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Persisting the `KernelEnrollmentId` already returned during `ScreenParticipant` synchronization eliminates redundant lookups, aligns `ExecutionDbSeeder` deterministically with `DevelopmentDemoData`, and pairs cleanly with full 38-kind `KernelScopeGraph` resolution so every `NodeRef` in Clinical Canvas resolves, masks, and zooms consistently.

## 5. Consequences & Trade-offs

- **Positive Impact:** Every `NodeKinds.All` identifier resolves via `/api/v1/kernel/nodes/{nodeRef}` and `/path`; collection-stage `validation_finding` zoom paths traverse `tenant -> study -> country -> study_site -> enrollment -> collection_instance -> validation_finding`; missing intermediate target rows degrade gracefully to `FallbackParent`/`tenant`; and clicking any `enrollment:<uuid>` chip in Clinical Canvas opens the full participant inspector with state transitions, casebook links, `Act` commands, and `Trace`/`Impact` lineage.
- **Negative Impact / Technical Debt:** Post-screening Execution state transitions (`Withdraw`, `Unblind`, `ScreenFail`) remain governed by `SubjectApplicationService` until full command-level lifecycle convergence.
- **Mitigation Strategy:** `SubjectApplicationService.ResolveSubjectAsync` accepts `subject_code`, `SubjectId`, `participant:<uuid>`, and `enrollment:<uuid>`, keeping both canonical and legacy callers interoperable.

## 6. Implementation & Verification

- **Affected Packages / Services:** `packages/kernel`, `apps/execution`, `apps/web`, `docs/openapi`, `docs/schema`, `docs/api`.
- **Verification Plan:** Verified by `KernelNavigatorTests`, `GrantNodeVisibilityTests`, `SubjectApplicationServiceCreateTests`, `KernelApiHostPostgresTests.HostNodeReadsResolveAll38NodeKindsEnforceMaskingAndCollectionFindingPathsAndFallBackWhenParentMissing`, `SubjectListPostgresTests`, `Cadence.ArchitectureTests`, and `apps/web` Vitest suite `StudyCanvasEnrollmentNodeRefs.spec.ts`.
