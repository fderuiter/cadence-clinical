# ADR-2543: The Kernel Host Seeds the Local Demo Study

- **Status:** Accepted
- **Date:** 2026-10-19
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Issue:** #5452
- **Requirements:** PRD-CRF-013 (tenant data isolation: the seed writes only the development tenant), PRD-SYS-001
  (every seeded record has its audit entry)
- **Amends:** [ADR-2542](2026-10-19-the-kernel-host-provisions-the-development-tenant.md) §4 ("Grants are not
  provisioned")

---

## 1. Context & Problem Statement

ADR-2542 provisions the `cadence-local` tenant, so a local stack serves kernel reads. But the tenant is empty and the
local realm's users hold no grants. A reader with no grant sees nothing (ADR-2457), so every Canvas read (activity,
search, Trace, Impact, Explain) returns no data or only masked nodes. The local stack can show the Canvas's shape but
not its content.

Two gaps caused this. Grants name a subject, and Keycloak generated a new subject id for each realm user on every
import. No command could create a study, site or study site until #5485.

## 2. Decision Drivers & Constraints

- **The command path (ADR-2449):** demo records must have the same rows, events and audit entries as real ones. Hand
  SQL is not an option.
- **Development only (ADR-2542):** the host's platform operator exists only with the development tenant setting, which
  the host refuses outside Development.
- **Restarts:** every local start runs the bootstrap again, and a start may stop halfway.
- **One study across services:** the Canvas shows Execution's participant panes beside the kernel's panes for the
  same study code, so both must describe the same study.

## 3. Options Considered

### Option 1: A CLI or wizard step after sign-in

- **Pros:** ✅ No change to the host.
- **Cons:** ❌ The CLI has no kernel persistence (K-XS-2). ❌ Every fresh stack needs an extra manual step.

### Option 2: Grant by role claim instead of subject

- **Cons:** ❌ The kernel grant model names an actor (spec §9.3). Granting realm roles would be a new authority source
  that only the local stack needs.

### Option 3: Fixed realm subject ids, and the bootstrap seeds through the pipeline (selected)

- **Pros:** ✅ Every record goes through the kernel's own commands. ✅ Grants follow the normal model. ✅ Turned on
  with one setting.
- **Cons:** ❌ The development operator writes more than the tenant row, so it needs a grant of its own.

## 4. Decision Outcome

- **Chosen Option:** Option 3.
- **Setting:** `Kernel:DevelopmentTenant:DemoData` (`Kernel__DevelopmentTenant__DemoData`), false by default. It is
  read only when `Kernel:DevelopmentTenant:Code` is set, so it shares that setting's Development-only guard. docker
  compose and the AppHost set it to `true`.
- **Fixed subject ids:** each user in `docker/cadence-realm.json` has a fixed `id`, which Keycloak keeps as the
  token's `sub`. The gateway forwards it and the kernel uses it as the actor (`Actor.Person(sub)`).
- **What is seeded,** after the tenant exists:
  - the sponsor organization `CADENCE-PHARMA` and the study `CADENCE-101` (the study the Execution service seeds);
  - two sites, `SITE-101` and `SITE-102`, each with its institution, linked to the study;
  - participants `SUBJ-101-001` and `SUBJ-101-002` screened at `SITE-101`, and `SUBJ-102-001` at `SITE-102`;
  - seven roles and one grant for each realm user:
    - `crc.site101`, `pi.site101` (added by #5463 to sign forms) and `cra.monitor` at the `SITE-101` study site, the
      site in their `site_id` attribute;
    - everyone else on the whole study.
- **How it is written:** the bootstrap's service account first defines a `DEMO_SEEDER` role and grants it to itself,
  tenant-wide, because a platform operator holds only `tenant.provision`, `grant.issue` and `role.define` without a
  grant (ADR-2455). Every later command is authorized by that grant.
- **Restarts replay:** every id is derived from the tenant and the record's name (a version 8 UUID over SHA-256).
  Every timestamp is fixed (valid time from 2026-01-05). Every idempotency key is fixed (`development-demo:<record>`).
  So each command carries the same payload on every start. A later start replays the stored results and writes
  nothing more, and a start interrupted halfway finishes the rest.
- **Role capabilities** are existing command capabilities. The kernel has no read-only capability, and any grant in
  effect makes its scope visible (ADR-2457), so the auditor's and safety officer's roles carry the nearest review
  capabilities.

## 5. Consequences & Trade-offs

- **Positive Impact:** on a fresh local stack, a realm user sees the demo study in the Canvas. The coordinator sees
  their own site's participants, and the other site stays masked. Every record has its event and audit entry.
- **Negative Impact / Technical Debt:**
  - The development operator grants itself a tenant-wide role.
  - Keycloak imports a realm only when it is missing, so a Keycloak that imported the realm before this change keeps
    its generated ids until its data is reset.
- **Mitigation Strategy:** the host still refuses the tenant settings outside Development (ADR-2542). The seed's
  grants are ordinary grants, visible and revocable like any other.
- ADR-2542 §4 is amended: grants for the realm's users are provisioned when demo data is on.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/kernel-api` (`DevelopmentDemoData`, `DevelopmentTenantBootstrap`),
  `docker/cadence-realm.json`, `docker/docker-compose.yml`, `apps/apphost`.
- **Verification Plan:**
  - `KernelDevelopmentTenantPostgresTests.DemoData_GrantsTheCoordinatorTheirSiteOnly_AndReplaysOnRestart` runs the
    real host on PostgreSQL as the application role. The coordinator finds `CADENCE-101` and lists their site's
    participants, the other study site is masked, and a person with no grant finds nothing. The grantees are exactly
    the realm's subject ids plus the seeder. A second start adds no audit entry.
  - `KeycloakRealmTenantTests.EveryRealmUser_HasAFixedSubjectId` and `AppHostModelTests.Kernel_ProvisionsTheLocalRealmsTenant`
    cover the realm and the wiring.
