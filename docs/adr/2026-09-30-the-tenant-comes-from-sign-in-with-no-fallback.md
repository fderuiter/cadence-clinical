# ADR-2514: The Tenant Comes From Sign-In, With No Fallback

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter, Claude Code
- **Deciders:** @fderuiter (chose "Tenant from sign-in" on #5038)
- **Requirements:** PRD-CRF-013 (site and tenant data isolation)
- **Issue:** #5038

---

## 1. Context & Problem Statement

The gateway signs a tenant code onto every proxied request (`X-Tenant-Id`, ADR-2256). The kernel host maps that code
to a kernel tenant and refuses a caller with no code, or a code that names no active tenant: "there is no fallback
tenant" (ADR-2540, `DirectoryTenantResolver`).

That promise did not hold end to end:

- The Keycloak realm put no tenant in tokens.
- The gateway (`GatewayIdentityForwarding`) and the downstream authentication handler (`GatewayAuthenticationHandler`)
  both replaced a missing tenant with `tenant_default`.

So every caller arrived as `tenant_default`, and the kernel could not tell that fallback from a real tenant code. With
more than one tenant, a user without a tenant would act in whichever kernel tenant has the code `tenant_default`.

## 2. Decision Drivers & Constraints

- A caller's tenant comes from something the identity provider asserts, never from a default or from the request.
- The kernel's "no fallback tenant" rule holds as written: a caller without a tenant reaches no tenant.
- Local stacks keep working with the realm's test users.
- The legacy services keep their own internal `tenant_default` columns until they move to kernel tenants. Nothing
  downstream but the kernel host reads the signed tenant.

## 3. Options Considered

### Option 1: Tenant from sign-in (selected)

- **Overview:** the realm maps a `tenant_id` user attribute into the access token. The gateway forwards the token's
  tenant, or none. The authentication handler turns no tenant into no tenant claim, and the kernel host answers 403.
- **Pros:**
  - ✅ The no-fallback rule is true from the token to the kernel.
  - ✅ This is how multi-tenant sign-in has to work anyway.
- **Cons:**
  - ❌ Every user needs a tenant attribute. A user without one can use no kernel route.

### Option 2: Keep one tenant, refuse the fallback code

- **Overview:** keep the default, but have the kernel host and tenant provisioning refuse the code `tenant_default`.
- **Cons:**
  - ❌ The default stays in the gateway and the handler, and every caller is still placed in it.

### Option 3: Leave as is

- **Cons:**
  - ❌ The kernel's isolation promise stays untrue.

## 4. Decision Outcome

- **Chosen Option:** Option 1, chosen by the maintainer on #5038.
- **Justification:** It is the only option where a caller's tenant is asserted by the identity provider and never
  assumed.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - `GatewayIdentity.TenantId` is null when the token names no tenant, and `GatewayIdentity.DefaultTenantId` is gone.
  - The gateway forwards `X-Tenant-Id` only when the token has a `tenant_id`, and it drops a caller-sent `X-Tenant-Id`
    as before. The signature covers the tenant either way.
  - A signed request without a tenant authenticates with no `tenant_id` claim, and the kernel host refuses it with
    "Your sign-in names no tenant."
- **Negative Impact / Technical Debt:**
  - Each realm user needs a `tenant_id` attribute. The local realm gives its test users `cadence-local`, so the local
    stack's kernel tenant is provisioned with that code.
  - The legacy services' own `tenant_default` values (eISF, eConsent, imaging and audit payload defaults) are
    untouched. They go when those services move to kernel tenants (#4787).
- **Mitigation Strategy:** `KeycloakRealmTenantTests` fails if the realm stops mapping the tenant or a realm user has
  none.

## 6. Implementation & Verification

- **Affected code:**
  - `packages/security`: `GatewayIdentity` and `GatewayAuthenticationHandler`.
  - `apps/gateway`: `GatewayIdentityForwarding`.
  - `docker/cadence-realm.json`: the `tenant` protocol mapper on `cadence-web`, and a `tenant_id` attribute on each
    user.
  - `packages/testing`: `TestSecurityContext.TenantCode` on test identities.
- **Verification:**
  - `GatewayAuthenticationTests.ProxiedRoute_WithATokenNamingNoTenant_ForwardsNoTenant`.
  - `ProxiedRoute_WithValidToken_ForwardsSignedIdentityFromTokenOnly`: the token's tenant wins over a caller-sent one.
  - `KernelApiHostTests.ReadWithoutATenantCode_IsRefused`: a tenantless signature is refused before the tenant
    directory is asked.
  - `KeycloakRealmTenantTests`.
