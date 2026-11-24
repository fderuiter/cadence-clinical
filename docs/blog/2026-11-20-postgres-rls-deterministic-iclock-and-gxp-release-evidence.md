---
title: "Defense-in-Depth PostgreSQL RLS, Deterministic IClock, and Forwarded GxP Evidence"
date: "2026-11-20T10:00:00Z"
authors:
  - "@fderuiter"
summary: "How we extended forced PostgreSQL Row-Level Security across all 15 standalone schemas, closed the TimeProvider wall-clock loophole, unified Part 11 session and signing primitives, shipped audited SOP escalation, and wired verified PostgreSQL 18 GxP evidence into release pipelines."
tags:
  - "security"
  - "postgresql"
  - "gxp"
  - "compliance"
  - "ci-cd"
related:
  - text: "ADR-2548: Standalone PostgreSQL Forced RLS & Scope Guards"
    link: "../adr/2026-11-20-standalone-postgres-rls-and-shared-scope-guards.md"
  - text: "ADR-2818: Clinical & Gateway Endpoints Take Time From IClock"
    link: "../adr/2026-11-20-clinical-and-gateway-endpoints-take-time-from-iclock-and-ban-timeprovider.md"
  - text: "ADR-2551: Shared Session, Sign-Out Guard & Part 11 Primitives"
    link: "../adr/2026-11-20-share-session-logout-and-signature-primitives.md"
  - text: "ADR-2641: Contextual SOP Resolution & Audited Ticket Escalation"
    link: "../adr/2026-11-20-contextual-sop-resolution-and-audited-ticket-escalation.md"
  - text: "ADR-2535: Forward Verified CI GxP Evidence to Releases"
    link: "../adr/2026-11-20-forward-verified-ci-gxp-evidence-and-sync-service-migrations.md"
  - text: "Companion: Trial Bootstrap, ODM/USDM Import, and Demos"
    link: "./2026-11-20-self-service-trial-bootstrap-protocol-import-and-cohosted-demos.md"
  - text: "Clock Migration Backlog"
    link: "../kernel/CLOCK_MIGRATION_BACKLOG.md#what-is-done"
  - text: "Clinical Kernel Package README"
    link: "../../packages/kernel/README.md"
draft: false
prev: false
next: false
---

# Defense-in-Depth PostgreSQL RLS, Deterministic IClock, and Forwarded GxP Evidence

Regulated clinical systems earn trust when security, timekeeping, electronic signatures, and release qualification are enforced mechanically rather than by convention. While our Clinical Kernel (`kernel.*`) already enforced forced PostgreSQL Row-Level Security, bitemporal audit chains, and injected clocks, several standalone services, browser session flows, and CI release workflows still carried subtle gaps.

Here is how five November 20, 2026 architectural decisions closed those gaps end-to-end across PostgreSQL, ASP.NET Core, Vue workspaces, and GitHub Actions.

## Forced PostgreSQL Row-Level Security Across All 15 Standalone Schemas

Until every standalone service consolidates into the single composition host, 15 standalone ASP.NET Core `DbContext` schemas (`ctms`, `econsent`, `eisf`, `etmf`, `edc`, `fileshare`, `imaging`, `interop`, `knowledge`, `notifications`, `org`, `payments`, `quality`, `safety`, and `tickets`) persist operational records alongside `kernel`. Relying on application-layer LINQ filters alone meant a raw SQL query or wildcard `*` study scope could risk cross-tenant exposure.

In [ADR-2548](../adr/2026-11-20-standalone-postgres-rls-and-shared-scope-guards.md), we brought database-enforced multi-tenant isolation and shared clinical scope guards to every standalone service without violating `K-XS-2` package boundaries:

- **Database-enforced `FORCE ROW LEVEL SECURITY`:** Every tenant-scoped standalone table now carries `tenant_id uuid NOT NULL` governed by `ENABLE` and `FORCE ROW LEVEL SECURITY` (`tenant_id = kernel.current_tenant_id()`) under the non-superuser `cadence_app` (`NOSUPERUSER NOBYPASSRLS`) role.
- **Session-bound interceptors:** `StandaloneTenantConnectionInterceptor` binds `set_config('cadence.tenant_id', ...)` on connection open and resets it on close, while `StandaloneTenantSaveChangesInterceptor` stamps verified tenant UUIDs and fails closed if no active tenant is bound.
- **Shared `ClinicalScopeGuard`:** `GatewayAuthenticationHandler` verifies active tenants via `kernel.tenant_id_by_code(@code)`, and standalone routes enforce `RequireTenantScope`, `RequireCallerStudyScope`, and `RequireCallerStudyAndSiteScope` verified by `RouteAuthorizationTests`.

## Closing the `TimeProvider` Loophole on `IClock` (`K-TIME-3`)

Kernel rule `K-TIME-3` requires clinical, security, and compliance code to read timestamps from `Cadence.Kernel.Time.IClock` so audit records and deterministic tests never drift from request logic. Although `BannedSymbols.txt` already banned `DateTime.Now`/`UtcNow` and `DateTimeOffset.Now`/`UtcNow`, .NET's `System.TimeProvider.System` and `TimeProvider.GetUtcNow()` remained unbanned—allowing endpoints in `econsent`, `knowledge`, `intelligence`, and `gateway` to read wall-clock time while audit interceptors in the same pipeline read `IClock`.

[ADR-2818](../adr/2026-11-20-clinical-and-gateway-endpoints-take-time-from-iclock-and-ban-timeprovider.md) migrated `EconsentEndpoints`, `TrainingEndpoints`, `BiometricEndpoints`, `SignatureTokenEndpoints`, and `GatewayIdentityForwarding` onto `IClock`, replaced bespoke `TestTimeProvider` doubles with `FakeClock`, and added `P:System.TimeProvider.System` and `M:System.TimeProvider.GetUtcNow` to `BannedSymbols.txt`. With `TreatWarningsAsErrors=true`, Roslyn rule `RS0030` blocks any unapproved wall-clock read at compile time, leaving only three narrowly justified framework adapter pragmas enforced by `WarningSuppressionPolicyTests` (tracked in the [Clock Migration Backlog](../kernel/CLOCK_MIGRATION_BACKLOG.md#what-is-done)).

## Shared OIDC Session Resilience, Draft-Safe Sign-Out, and Part 11 Signing

Across `apps/web` and `apps/subject-portal`, concurrent API calls during token expiry could previously trigger duplicate `keycloak.updateToken()` refreshes, voluntary sign-out purged local drafts without warning, and seven step-up electronic signature callers built `/api/v1/auth/signature-verification` payloads separately.

[ADR-2551](../adr/2026-11-20-share-session-logout-and-signature-primitives.md) centralized all three primitives inside `packages/ui`:

1. **Single-flight OIDC refresh (`createOidcSessionCoordinator`):** Coalesces concurrent token refreshes into one promise, retries `401` responses once with a fresh token, and transitions expired sessions cleanly without wiping local eCRF or PIN-encrypted IndexedDB questionnaire drafts.
2. **Draft-safe voluntary sign-out (`useSignOutGuard` & `SignOutConfirmModal`):** Flushes debounced draft writes and prompts for WCAG 2.1 AA focus-trapped confirmation before purging shared-workstation storage on voluntary sign-out.
3. **Canonical Part 11 v3 step-up helpers (`packages/ui/signing.js`):** Standardizes `semantic_action` validation (including `ctms.doa.sign_off`), custom `"Other"` reason checks, claim-derived signer role defaults, credential wiping on submit/cancel/`Escape`, and deterministic `subject.site101` participant realm seeding under K027 isolation.

## Contextual SOP Specificity Scoring and Audited Ticket Escalation

Clinical staff working on `/studies`, `/ecrf`, `/ctms`, and `/ssu` use the **Help & SOPs** drawer (`ContextualHelpDrawer.vue`) to consult controlled procedures and escalate blockers. Previously, `apps/knowledge` and `apps/tickets` exposed only `/status` stubs, initial mounts at `/ecrf` skipped loading SOP guidance, failed lookups masked server errors as empty states, and legacy module-local audit tables sat outside the shared SHA-256 Merkle chain.

[ADR-2641](../adr/2026-11-20-contextual-sop-resolution-and-audited-ticket-escalation.md) delivered dedicated minimal APIs and generated OpenAPI clients for both services:

- **4-tier route specificity scoring:** `ContextualHelpScorer` resolves locked SOP snapshots deterministically across exact routes, parameterized routes (`/studies/:studyCode`), prefix wildcards (`/studies/*`), and universal fallbacks (`*`), weighted by normalized persona and priority, with accessible `role="alert"` retry recovery on transport errors.
- **De-identified, Merkle-audited ticket escalation:** `apps/tickets` enforces gateway `X-Change-Reason` headers, rejects direct participant PII via `TicketPiiValidator` (while permitting pseudonymous subject codes), computes Dual-Target SLA timestamps from `IClock`, and writes every mutation into the shared `audit_log_entries` SHA-256 hash chain via `GxPAuditInterceptor`.

## Forwarding Verified PostgreSQL 18 GxP Evidence into Release Archives

Finally, [ADR-2535](../adr/2026-11-20-forward-verified-ci-gxp-evidence-and-sync-service-migrations.md) closed the loop between pull-request verification and release qualification:

- **Forwarded PostgreSQL 18 evidence:** `dotnet-tests` validates TRX assemblies, `*.database.txt` PostgreSQL 18 fixture metadata, and zero skipped non-opt-in database tests via `scripts/check-database-test-results.sh` before uploading `<run_id>-<run_attempt>`-scoped artifacts consumed directly by `compliance` (`gxp sync --results-dir`) and `production-pipeline.yml` (`gxp snapshot --results-dir`).
- **Gated documentation & release workflows:** `ci.yml` gates both `production-pipeline.yml` and `deploy-docs.yml` behind passing validation jobs on `main` pushes while preserving standalone `workflow_dispatch` PostgreSQL 18 qualification.
- **Synchronized EF Core migrations & deterministic harnesses:** Synchronized migration snapshots for `ctms` (`AddTransactionalOutbox`), `tickets` (`EnforceTicketStatusCheckConstraint`), and `payments` (`AddPaymentStatusCheckConstraints`), alongside deterministic MassTransit `IConsumeObserver` harnesses and circular-ESM-safe browser initialization.
