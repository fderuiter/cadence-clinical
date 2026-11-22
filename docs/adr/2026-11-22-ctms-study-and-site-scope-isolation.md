# ADR-2644: CTMS Study and Site Scope Isolation Across DOA and Monitoring Letter Routes

- **Status:** Accepted
- **Date:** 2026-11-22
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-192

---

## 1. Context & Problem Statement

While `ADR-2548` and `ADR-2642` introduced PostgreSQL `FORCE ROW LEVEL SECURITY` (`tenant_id`) and baseline `CtmsScopeGuard` checks across `Cadence.Apps.Ctms`, three CTMS operational tables (`ctms_delegations`, `ctms_generated_letters`, and `ctms_audit_logs`) lacked explicit persisted `StudyId` and `SiteId` scope columns. As a result:

1. `CtmsDelegation` stored only `SiteId`, causing cross-study delegation reads, sign-offs, revocations, and PDF exports whenever a shared clinical site participated in multiple studies within a tenant, and omitting delegations-only sites from study discovery in `CtmsScopeGuard.RequireDoaSiteScopeAsync`.
2. `CtmsAuditLog` lacked `StudyId` and `SiteId` columns and relied on `Details.Contains(siteId)` substring matching on DOA log and PDF export routes, allowing prefix-colliding site codes (`SITE-1` matching `SITE-101`) to leak foreign audit entries.
3. `CtmsGeneratedLetter` relied on parsing `PdfObjectKey` (`letters/{studyId}/{siteId}/...`), leaving draft or unkeyed letters (`PdfObjectKey == null`) unguarded on `GET /api/v1/ctms/letters/{letterId}/pdf`, while `POST /api/v1/ctms/letters/compile` accepted unvalidated caller-supplied `VisitId` values and `POST /api/v1/ctms/letters/render` enforced only study scope (`RequireCallerStudyScope`) instead of study-and-site scope for site-bound roles.

## 2. Decision Drivers & Constraints

- **Fail-Closed Study & Site Isolation (`PRD-SYS-192`, `ADR-2548`):** Every CTMS DOA and monitoring-letter route must verify caller baseline scope, site assignment, and study assignment before revealing site existence or returning domain records, returning RFC 7807 `403 Forbidden` (`FORBIDDEN_SCOPE`) on unauthorized probes and `404 Not Found` only when an authorized caller targets a nonexistent record.
- **Exact Relational Scope Filtering:** DOA log and PDF export queries must filter `CtmsDelegations`, `CtmsSiteGreenlightGates`, and `CtmsAuditLogs` by exact `SiteId` and authorized `StudyId` values rather than substring text matching.
- **Immutable Audit Content (`21 CFR Part 11`):** Backfilling historical `ctms_audit_logs` rows during schema migration must populate new scope metadata columns (`StudyId`, `SiteId`) without mutating recorded `Details` or `Timestamp` values, and preserve `FORCE ROW LEVEL SECURITY` on all CTMS tables.

## 3. Options Considered

### Option 1: Persist Indexed `StudyId` and `SiteId` Columns with Fail-Closed `CtmsScopeGuard` Enforcement (Selected)

- **Overview:** Add required indexed `StudyId` to `CtmsDelegation`, required indexed `StudyId` and `SiteId` to `CtmsGeneratedLetter`, and nullable indexed `StudyId` and `SiteId` to `CtmsAuditLog` with an EF Core migration that backfills unambiguous existing rows per `tenant_id` while preserving `FORCE ROW LEVEL SECURITY`. Expand `CtmsScopeGuard.RequireDoaSiteScopeAsync` and `DoaEndpoints.SiteExistsAsync` across all ten study-scoped CTMS tables, support optional explicit `study_id` disambiguation on DOA routes, enforce `RequireStudyAndSite` on delegation sign-off/revoke and letter PDF download, validate explicit `VisitId` ownership on letter compilation, and require `RequireCallerStudyAndSiteScope` plus `ClinicalScopeGuard.RequireStudyAndOptionalSite` on `/letters/render`.
- **Pros:**
  - Eliminates cross-study and cross-site BOLA/IDOR paths across all five DOA routes and three monitoring-letter routes.
  - Replaces brittle `Details.Contains(siteId)` and `PdfObjectKey` string parsing with indexed relational predicates.
  - Preserves existing audit payload immutability and PostgreSQL tenant RLS guarantees.
- **Cons:**
  - Adds five temporary `StudyId`/`SiteId` scalar properties to `Cadence.Apps.Ctms` tracked in `kernel-boundaries.baseline.txt` until standalone CTMS aggregates migrate onto kernel study/site identifiers.

### Option 2: Infer Scope Dynamically at Read Time Without Schema Changes

- **Overview:** Continue inferring delegation and audit scope from joins to `ctms_site_greenlight_gates` and regex matching over `CtmsAuditLog.Details`.
- **Pros:**
  - Avoids an EF Core schema migration on `CtmsDbContext`.
- **Cons:**
  - Cannot disambiguate delegations or audit rows at multi-study shared sites and leaves unkeyed `CtmsGeneratedLetter` drafts without authoritative study/site ownership.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Persisting indexed `StudyId` and `SiteId` columns on `CtmsDelegation`, `CtmsGeneratedLetter`, and `CtmsAuditLog` provides deterministic, fail-closed relational scope enforcement across every DOA and letter endpoint while preserving GxP audit integrity and tenant RLS isolation.

## 5. Consequences & Trade-offs

- **Positive Impact:** Shared-site investigators and CRAs are strictly isolated to their authorized studies and sites; prefix-colliding site IDs (`SITE-1` vs `SITE-101`) never leak audit trails; unkeyed generated letters and foreign `VisitId` references fail closed; and OpenAPI + schema docs stay synchronized.
- **Negative Impact / Technical Debt:** `kernel-boundaries.baseline.txt` records the five new `StudyId`/`SiteId` properties on CTMS entities until CTMS converges with kernel grant/study value objects.
- **Mitigation Strategy:** Enforced via `CtmsStudySiteScopeIsolationTests` (in-memory host) and `CtmsStudySiteScopePostgresTests` (PostgreSQL `cadence_app` with `FORCE ROW LEVEL SECURITY` and migration backfill verification).

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/ctms`, `apps/web/src/api/generated/ctms.ts`, `docs/openapi/ctms_openapi.json`, `docs/schema/ctms.md`, `tests/Cadence.UnitTests`, `tests/Cadence.IntegrationTests`, `tests/Cadence.ArchitectureTests`.
- **Verification Plan:** Verified via `CtmsStudySiteScopeIsolationTests`, `CtmsStudySiteScopePostgresTests`, `RenderRouteAuthorityTests`, `DoaEndpointsTests`, `ServiceSchemaPostgresTests`, and `Cadence.ArchitectureTests`.
