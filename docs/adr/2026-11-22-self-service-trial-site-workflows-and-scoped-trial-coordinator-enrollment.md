# ADR-2822: Self-Service Trial Site Workflows and Scoped Trial Coordinator Enrollment

- **Status:** Accepted
- **Date:** 2026-11-22
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001, PRD-SYS-004, PRD-SUB-001

---

## 1. Context & Problem Statement

Following `ADR-2547` and `ADR-2604`, newly self-registered evaluators could bootstrap an isolated 30-day `Trialing` workspace (`trial-<12hex>`) with `Sponsor Admin` and `Sponsor Designer` roles and create a study (`POST /api/v1/kernel/studies`). However, evaluating **Blueprint** and **Capture** end-to-end inside a self-service trial workspace hit four blockers:

1. **No Self-Service Site or Study-Site Registration Surface:** `Cadence.Kernel.Api` had no HTTP endpoints to list or register clinical sites (`Site`, `OrganizationKind.SiteInstitution`) or link sites to a study (`StudySite`), causing participant screening synchronization (`POST /api/v1/kernel/enrollments/screen`) to fail with `StudySiteUnknown`, and leaving `StudyCanvasView.vue`, `SiteActivationPanel.vue`, and `AddParticipantForm.vue` without linked-site workflows.
2. **Trial Founder Participant Permission Gap:** `Sponsor Admin` (`sponsor_dm`) and `Sponsor Designer` (`sponsor_designer`) deliberately lack `subject_enrollment:create` and `subject_enrollment:update` under 21 CFR Part 11 separation of duties (`PRD §3.2`), causing solo trial evaluators to receive `403 Forbidden` on `POST /api/v1/execution/subjects`, while the `execution-forms` trial service account also lacked `participant.register`.
3. **Production Separation-of-Duties Boundary:** Granting `sponsor_dm` subject write permissions globally would violate separation of duties in production sponsors, while adding a site-scoped role to `SiteAccessEvaluator.SITE_SCOPED_ROLES` would block solo founders whose tokens carry wildcard study access (`study_ids: ["*"]`) without explicit `site_id` claims.
4. **Cross-Tenant `ClinicalSubject` Unique Index Collisions:** Although `ExecutionDbContext` enforced PostgreSQL `FORCE ROW LEVEL SECURITY` by `tenant_id` (`ADR-2548`), `clinical_subjects` retained global unique indexes on `SubjectCode` and `(StudyCode, EnrollmentIndex)` and an unscoped `pg_advisory_xact_lock(hashtext(StudyCode))`, causing cross-tenant `23505` unique constraint violations when two trial workspaces both evaluated `CADENCE-101` and enrolled `SUBJ-001`.

## 2. Decision Drivers & Constraints

- **Kernel Alignment & `SiteInstitution` Integrity (`K-SCOPE-1`, `PRD-SYS-001`):** Self-service site routes must dispatch canonical kernel commands (`RegisterOrganization` with `OrganizationKind.SiteInstitution`, `RegisterSite`, `LinkSiteToStudy`) with `Idempotency-Key` and signed `X-Change-Reason`, disambiguating derived institution codes (`-SITE`) when a founder names their sponsor and site institution identically rather than hardcoding `OrganizationKind.Sponsor`.
- **Preserving Production Separation of Duties (`PRD-SYS-004`, `PRD §3.2`):** `sponsor_dm` must remain read-only for source subject mutations, and self-service coordinator writes in `Execution` must be rejected outside verified `trial-` workspaces minted by `TrialOnboardingService`.
- **True Multi-Tenant Subject Isolation (`ADR-2548`, `PRD-SUB-001`):** `clinical_subjects` uniqueness and sequential enrollment advisory locks must be scoped per `tenant_id` without weakening `FORCE ROW LEVEL SECURITY`.

## 3. Options Considered

### Option 1: Grant `sponsor_dm` Subject Enrollment Writes Globally

- **Overview:** Add `subject_enrollment:create` and `subject_enrollment:update` to `ROLE_SPONSOR_DM` and `SubjectAuthority`.
- **Pros:**
  - Requires no new Keycloak realm role.
- **Cons:**
  - Violates GxP separation of duties (`PRD §3.2`) by allowing sponsor data managers in production tenants to create and mutate clinical source subjects.

### Option 2: Kernel Site Endpoints, Scoped `Trial Coordinator` Role Restricted to Verified `trial-` Workspaces, and Tenant-Scoped `ClinicalSubject` Indexes (Selected)

- **Overview:** Expose `GET/POST /api/v1/kernel/sites` and `GET/POST /api/v1/kernel/studies/{studyRef}/sites` over `IStudyDirectory` and kernel command handlers (`OrganizationKind.SiteInstitution`); surface a `Conduct -> Sites` canvas section (`SitesSection.vue`) plus linked-site pickers and inline site registration shortcuts in `AddParticipantForm.vue` and `SiteActivationPanel.vue`; provision a `Trial Coordinator` (`trial_coordinator`) realm role during `TrialOnboardingService` (alongside `participant.register` on `execution-forms`), keeping `trial_coordinator` outside `SiteAccessEvaluator.SITE_SCOPED_ROLES` and restricting its participant write authority in `SubjectAuthority` strictly to verified `trial-<12hex>` tenant codes (`^trial-[0-9a-f]{12}\z`); and migrate `clinical_subjects` unique indexes and `AdvisoryLockSql` enrollment locks to `(tenant_id, ...)`.
- **Pros:**
  - Unblocks zero-operator self-service trial evaluation across study creation, clinical site setup, site activation, participant enrollment, and eCRF capture.
  - Strictly preserves `sponsor_dm` separation of duties and blocks `trial_coordinator` writes in non-trial tenants (`403 Forbidden`).
  - Eliminates cross-tenant `23505` collisions and cross-tenant advisory lock contention on `clinical_subjects`.
- **Cons:**
  - Introduces an explicit `trial-` tenant guard in `SubjectAuthority` until Execution subject endpoints migrate onto Trust engine `Grant` evaluation (`#4781`).

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Option 2 enables end-to-end self-service trial evaluation on real kernel and execution persistence while keeping production separation of duties, site scope rules, and tenant row-level isolation intact.

## 5. Consequences & Trade-offs

- **Positive Impact:** Self-registered founders can register `SiteInstitution` sites, link them to studies in Clinical Canvas, select or inline-register sites during activation and participant enrollment, enroll participants without `StudySiteUnknown` or `403` errors inside their `trial-` workspace, and evaluate canonical sample codes (`CADENCE-101`, `SUBJ-001`) across concurrent tenants without `23505` collisions.
- **Negative Impact / Technical Debt:** `SubjectAuthority.AuthorizeParticipantChange` inspects the verified `CadenceClaimTypes.TenantId` claim against `^trial-[0-9a-f]{12}\z` alongside role normalization.
- **Mitigation Strategy:** `CallerIdentity` populates `AuthenticatedCaller.TenantId` exclusively from gateway-signed claims (`ADR-2256`), and unit plus PostgreSQL integration tests verify both positive `trial-` workflows and `403` denials for `sponsor_dm` and non-trial `trial_coordinator` callers.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/kernel` (`Abstractions`, `Persistence`, `Api`), `apps/kernel-api`, `packages/security`, `apps/execution`, `apps/web`, `docker/cadence-realm.json`, `docs/openapi/kernel_openapi.json`, `docs/schema/execution.md`.
- **Verification Plan:** Verified via `Cadence.ArchitectureTests`, `KernelStudyImportEndpointsTests`, `SubjectAuthorityTests`, `ClinicalSubjectConfigurationTests`, `SubjectRepositoryTests`, PostgreSQL integration tests (`KernelTrialOnboardingPostgresTests`, `TrialSubjectWorkflowPostgresTests`), and Vitest specs (`StudyCanvasSelfServiceSites.spec.ts`).
