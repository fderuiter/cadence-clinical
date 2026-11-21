# ADR-2549: Kernel Site Setup, Sandbox Site Provisioning, and Execution Tenant-Scoped Subject Uniqueness

- **Status:** Accepted
- **Date:** 2026-11-21
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SUB-001, PRD-SYS-004, PRD-SYS-192

---

## 1. Context & Problem Statement

After ADR-2547 introduced self-service trial onboarding (`POST /api/v1/kernel/onboarding/trial`) and ADR-2548 enforced PostgreSQL `FORCE ROW LEVEL SECURITY` across standalone schemas, four multi-tenant and clinical workflow gaps remained for freshly onboarded workspaces:

1. Trial onboarding provisioned a sponsor `Organization` and granted `sponsor_admin` and `sponsor_designer`, but created no `OrganizationKind.SiteInstitution`, no default sandbox `Site`, and no `Site Investigator` role or `site_id` attribute. Additionally, `Cadence.Kernel.Api` lacked self-service HTTP routes for `RegisterOrganization` (`SiteInstitution`), `RegisterSite`, `LinkSiteToStudy`, and `StudySite` queries, leaving new studies with zero investigational sites.
2. `KeycloakOnboardingClient` assigned wildcard `study_ids = ["*"]` during trial bootstrap instead of explicit per-study and per-site token attributes, and study/site creation did not synchronize newly registered study or site codes back to Keycloak.
3. In `apps/execution`, `ClinicalSubject` (`IX_clinical_subjects_study_id_subject_id`, `IX_clinical_subjects_study_id_enrollment_index`), `SubjectConsent` (`IX_subject_consents_study_id_subject_id_consent_version`), and `SiteComplianceCache` (`IX_site_compliance_cache_site_id`) retained global cross-tenant unique indexes without `tenant_id`, and `AdvisoryLockSql.StudyEnrollmentLockKey` hashed `studyId` globally without `tenantId`. Consequently, creating `CADENCE-101` and screening `SUBJ-101-001` (`enrollment_index = 0`) in a fresh tenant collided with `cadence-local` seeded rows on PostgreSQL unique constraints (`23505`).
4. In `apps/web`, `sponsor_admin` defaulted `canvasRole` to `sponsor_dm` (`Data Manager`, which is intentionally forbidden from modifying participant source entries by separation of duties) without offering a switchable `site_crc` Coordinator lens or pre-populated study-site selector when screening participants.

## 2. Decision Drivers & Constraints

- **Single Kernel Command Pipeline & Package Boundaries (`K-XS-1`, `K-MOD-1`, `ADR-2449`, `ADR-2600`):** Organization, site, and study-site mutations must execute strictly through `IKernelCommandSender` (`RegisterOrganization`, `RegisterSite`, `LinkSiteToStudy`) with `Idempotency-Key` and signed `X-Change-Reason`; standalone `Execution` must never duplicate `Site` or `StudySite` master tables.
- **Explicit Clinical Scope & Separation of Duties (`PRD-SUB-001`, `PRD-SYS-004`, `PRD-SYS-192`):** Trial founders receive explicit `site_id` and `study_ids` token attributes (never wildcard `*`), and `Data Manager` (`sponsor_dm`) remains read-only for participant source records (`SubjectAuthority.cs`), while founders holding `Site Investigator` can switch into a Coordinator (`site_crc`) lens for site-scoped screening.
- **Per-Tenant Identifier Independence (`K-ID-1`, `K-ID-2`, `ADR-2548`):** Distinct tenants must be able to register identical study codes (such as `CADENCE-101`), subject codes (`SUBJ-101-001`), enrollment indices (`0`), consent versions, and site compliance cache rows without cross-tenant unique-index collisions or advisory-lock contention, while `ExecutionDbSeeder` scopes demo rows strictly to `cadence-local`.

## 3. Options Considered

### Option 1: Duplicate `Site` / `StudySite` Tables in Execution and Prefix Tenant Codes onto `study_id`

- **Overview:** Create local site master tables inside `edc` and rewrite user-entered study codes to `<tenant_code>:<study_code>` in `Execution`.
- **Pros:**
  - Avoids migrating `edc` composite unique indexes.
- **Cons:**
  - Violates `K-XS-1` and `K-MOD-1` by duplicating kernel master entities in a standalone service and leaks internal tenant prefixes into clinical study identifiers and CDISC exports.

### Option 2: Kernel Site Setup Endpoints, Explicit Identity Scope Sync, Switchable Coordinator Lens, and Composite `(tenant_id, ...)` Execution Indexes (Selected)

- **Overview:** Extend `TrialOnboardingService` to deterministically provision a starter `SiteInstitution`, default sandbox `Site` (`SITE-SANDBOX-01`), and `site_investigator` role/grant, and assign `Site Investigator` plus explicit `site_id` (with empty initial `study_ids`) in `KeycloakOnboardingClient`; expose `POST /api/v1/kernel/organizations`, `GET/POST /api/v1/kernel/sites`, `GET/POST /api/v1/kernel/studies/{studyRef}/sites`, and one-step `link_sandbox_site` / `initial_site_ref` on `POST /api/v1/kernel/studies` with `IKernelIdentityScopeSynchronizer` (`GrantStudyScopeAsync`, `GrantSiteScopeAsync`); scope `ClinicalSubject`, `SubjectConsent`, and `SiteComplianceCache` domain entities, repositories, advisory locks, and EF Core unique indexes by `tenant_id`; and add `StudySitesPanel.vue` plus a switchable `site_crc` Coordinator lens in `apps/web`.
- **Pros:**
  - Preserves `K-XS-1`, `K-MOD-1`, `ADR-2449`, and `SubjectAuthority` separation of duties.
  - Eliminates wildcard `study_ids` during trial bootstrap and prevents cross-tenant `23505` collisions on `clinical_subjects`, `subject_consents`, and `site_compliance_cache`.
  - Lets trial founders link their sandbox site on study creation (or register custom sites under `Start -> Sites`) and screen participants immediately via the Coordinator lens.
- **Cons:**
  - Requires client token refresh (`authStore.refresh(-1)`) after study/site registration so newly synchronized `study_ids` and `site_id` claims appear on subsequent gateway-forwarded requests.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Routing site setup through the Clinical Kernel command pipeline, synchronizing explicit `study_ids` and `site_id` claims, and scoping Execution's subject/consent/compliance unique indexes and advisory locks by `tenant_id` deliver end-to-end site setup and screening for self-service workspaces while upholding `PRD-SUB-001`, `PRD-SYS-004`, `PRD-SYS-192`, `K-ID-1`, and `K-XS-1`.

## 5. Consequences & Trade-offs

- **Positive Impact:** New trial workspaces start with a ready-to-use sandbox institution and site, zero wildcard study leakage, full kernel site/study-site management in the Study Canvas, switchable Coordinator screening (`site_crc`) without weakening `Data Manager` (`sponsor_dm`) restrictions, and independent per-tenant `CADENCE-101` / `SUBJ-101-001` numbering in PostgreSQL.
- **Negative Impact / Technical Debt:** `KeycloakIdentityScopeSynchronizer` performs best-effort Admin REST updates on study and site creation when `KeycloakAdmin:BaseUrl` is configured until gateway authorization fully migrates to direct kernel grant evaluation (`#4781`).
- **Mitigation Strategy:** Deterministic UUIDv8 keys, idempotent command handlers, and client-side scope synchronization keep onboarding, study creation, and site registration retry-safe across live OIDC and offline test environments.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/kernel` (`Abstractions`, `Persistence`, `Api`), `apps/kernel-api`, `apps/execution`, `apps/web`, `docs/openapi`, `docs/schema`.
- **Verification Plan:** Verified via `KernelStudyImportEndpointsTests`, `CommercialAndOnboardingUnitTests`, `ClinicalSubjectConfigurationTests`, `Cadence.ArchitectureTests`, PostgreSQL 18 forced-RLS integration tests (`KernelTrialOnboardingPostgresTests`, `ExecutionTenantIsolationPostgresTests`), and Vitest (`StudyCanvasSitesAndTenancy.spec.ts`).
