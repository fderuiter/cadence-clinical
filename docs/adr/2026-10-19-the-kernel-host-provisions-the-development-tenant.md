# ADR-2542: The Kernel Host Provisions the Development Tenant

- **Status:** Accepted
- **Date:** 2026-10-19
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Issue:** #5430
- **Requirements:** PRD-CRF-013 (tenant data isolation: unprovisioned tenant codes stay refused), PRD-SYS-001 (the
  provisioning writes its audit entry)
- **Amends:** [ADR-2540](2026-09-30-kernel-api-host-and-read-endpoints-for-the-canvas.md) §4.4 ("the host never
  provisions a tenant, so it names no platform operators")

---

## 1. Context & Problem Statement

ADR-2514 gives every user of the local Keycloak realm the tenant code `cadence-local`, "so the local stack's kernel
tenant is provisioned with that code". Nothing provisioned it. `ProvisionTenant` was issued only by tests, so on a fresh
database docker compose and the Aspire AppHost answered every kernel read with 403 "Your sign-in's tenant is not an
active kernel tenant": the Clinical Canvas's activity stream, search, Trace, Impact and Explain all failed locally.

A tenant must be created through the kernel command pipeline, as a platform operator (ADR-2455), so that its row, its
`TenantProvisioned` event and its audit entry commit together. Hand-written SQL skips all three.

## 2. Decision Drivers & Constraints

- **ADR-2514:** the tenant comes from sign-in, with no fallback. A missing tenant must stay a refusal everywhere except
  the one code a local stack is configured to provision.
- **ADR-2540:** the kernel host is the only process with kernel persistence (K-XS-2), so it is the only place a
  provisioning step can run without a new database client.
- **ADR-2455:** only a platform operator holds `tenant.provision` without a grant. A deployed host must hold none.
- **Pre-deployment:** nothing is deployed, so this is about making the reference implementation run, not about tenant
  onboarding.

## 3. Options Considered

### Option 1: SQL in the local tooling

- **Pros:** ✅ No product code.
- **Cons:** ❌ No event, no audit entry, no pipeline: the local stack would run on a tenant the kernel never created.

### Option 2: A CLI command

- **Pros:** ✅ Explicit, one-off.
- **Cons:** ❌ The CLI would need kernel persistence and the command pipeline, which only the kernel host may register
  (K-XS-2). ❌ Every local start needs an extra step.

### Option 3: The kernel host provisions a configured development tenant on start (selected)

- **Pros:** ✅ The tenant is created by the kernel's own command path, with its event and audit entry. ✅ docker compose
  and the AppHost need only a setting.
- **Cons:** ❌ The host gains a platform operator, so the setting must be impossible outside Development.

## 4. Decision Outcome

- **Chosen Option:** Option 3.
- **Setting:** `Kernel:DevelopmentTenant:Code` (environment `Kernel__DevelopmentTenant__Code`), and optionally
  `Kernel:DevelopmentTenant:Name`. Unset, the host behaves as ADR-2540 describes: no bootstrap, no platform operator.
- **Development only:** with the setting in any other environment the host refuses to start.
- **On start**, after the migrations: if no active tenant has the code, the host sends `ProvisionTenant` through
  `IKernelCommandSender` as the service account `svc:kernel-development-bootstrap`, with the idempotency key
  `development-tenant:<code>`. That account is the host's only platform operator, and only while the setting is present.
- **The actor:** the bootstrap runs in its own DI scope and names its service account there (`ServiceActorScope`).
  Request scopes never set it, so a request's actor still comes only from the verified caller identity.
- **docker compose and the AppHost** set the code to `cadence-local`. The AppHost passes its own environment to the
  kernel, since a project resource does not inherit it, and sets the code only when that environment is Development.
- **Grants are not provisioned.** The realm's users have no fixed subject ids, so the local stack's users read with no
  grant, and the kernel masks what they may not see (ADR-2457).

## 5. Consequences & Trade-offs

- **Positive Impact:** a fresh local stack serves kernel reads for the realm's users, and the tenant they read has the
  same row, event and audit entry as any other. Other tenant codes are still refused.
- **Negative Impact / Technical Debt:** the kernel host has a platform operator in Development when configured. The
  realm's users still hold no grants, so the Canvas shows masked nodes until grants are provisioned too.
- **Mitigation Strategy:** the host refuses to start with the setting outside Development, and a test keeps it so.
- ADR-2540 §4.4 is amended: the host provisions one configured tenant in Development, and otherwise never.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/kernel-api` (`DevelopmentTenantBootstrap`, `ClaimsCurrentActor`),
  `apps/apphost`, `docker/docker-compose.yml`, `scripts/local_stack_nodocker.sh`.
- **Verification Plan:** `KernelDevelopmentTenantPostgresTests` runs the real host against PostgreSQL as the
  application role: two starts provision the tenant once, with its audit entry by `svc:kernel-development-bootstrap`,
  and other codes stay 403. `KernelApiHostTests.DevelopmentTenantSetting_OutsideDevelopment_StopsTheHost` and
  `AppHostModelTests.Kernel_ProvisionsTheLocalRealmsTenant` cover the guard and the wiring.
