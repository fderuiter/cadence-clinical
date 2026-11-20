# ADR-2547: Keycloak Self-Registration and Clinical Kernel Trial Bootstrap

- **Status:** Accepted
- **Date:** 2026-11-20
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001, PRD-SYS-004, PRD-SYS-192

---

## 1. Context & Problem Statement

Cadence Clinical previously restricted Keycloak sign-in to pre-seeded users in `docker/cadence-realm.json` (`registrationAllowed: false`) and restricted `tenant.provision` to `DevelopmentTenantBootstrap` (`svc:kernel-development-bootstrap`) in Development environments. Evaluators visiting the web workspace had no public `/plans` surface to inspect modular packaging (`Individual`, `Select`, `Complete`), could not self-register via OIDC PKCE, and could not bootstrap an isolated Clinical Kernel tenant without manual database seeding because `KernelTenantFilter` rejected authenticated callers lacking an active `tenant_id` token claim. Additionally, hidden routes linked to an internal issue tracker rather than explaining product-family entitlements and pre-release delivery status, and the kernel lacked commercial `Subscription` and `ProductEntitlement` models decoupled from clinical authority.

## 2. Decision Drivers & Constraints

- **Strict Commercial vs. Clinical Authority Separation:** Commercial subscriptions and product-family entitlements govern workspace packaging and trial mutation boundaries only; they must never confer clinical study visibility or bypass `AuthorityEvaluator`, `GrantNodeVisibility`, study scope, blinding, or participant consent (`PRD-SYS-004`, `PRD-SYS-192`).
- **Regulated Baseline Never Gated:** Audit trails, electronic signatures (`Attest`), authorization governance, and regulated record reads/exports remain active in Cadence Core across every tier and after trial expiration (`PRD-SYS-001`, `docs/strategy/PRODUCT_CATALOG.md`).
- **Pseudonymous Clinical Identity (K-ID-4):** `kernel.people` stores only pseudonymous identifiers and audit metadata; human names and email addresses remain outside `kernel.people`, while organization names live on `kernel.organizations` and regional residency lives on `kernel.tenants`.
- **Single Kernel Write Path & PostgreSQL RLS:** Trial provisioning must execute exclusively through `IKernelCommandSender` under an explicit platform operator (`svc:kernel-trial-onboarding`), remain strictly idempotent per OIDC `sub`, and synchronize `tenant_id` and initial roles back to Keycloak so refreshed tokens pass `GatewayIdentityForwarding`, `DirectoryTenantResolver`, and PostgreSQL Row-Level Security.

## 3. Options Considered

### Option 1: Store Commercial Plan Fields on `Tenant` and Bypass `KernelTenantFilter` Globally

- **Overview:** Add tier and trial expiration columns directly onto `kernel.tenants` and allow any kernel route to auto-create a tenant when `tenant_id` is absent.
- **Pros:**
  - Fewer domain entities and endpoints.
- **Cons:**
  - Conflates contractual/residency isolation (`Tenant`) with commercial lifecycle state.
  - Weakens fail-closed tenant resolution (`ADR-2514`) across clinical endpoints.

### Option 2: Dedicated `TrialOnboardingOperator`, Explicit Onboarding Route, and Separate `Subscription` / `ProductEntitlement` Entities (Selected)

- **Overview:** Enable Keycloak OIDC self-registration with PKCE `S256`; expose `/plans` and `/onboarding/trial` in `apps/web`; register `KernelAuthorityOptions.TrialOnboardingOperator` (`svc:kernel-trial-onboarding`) across environments; map `POST /api/v1/kernel/onboarding/trial` outside `KernelTenantFilter` while keeping all other kernel routes fail-closed; model `Subscription` and `ProductEntitlement` as tenant-isolated kernel entities with `[BaselineKernelCommand]` exemptions on regulated commands; and write `tenant_id` plus `Sponsor Admin` / `Sponsor Designer` roles back to Keycloak via Admin REST followed by token refresh.
- **Pros:**
  - Keeps `Tenant` focused strictly on isolation and data residency.
  - Preserves fail-closed `DirectoryTenantResolver` and PostgreSQL RLS on every clinical and entitlement route.
  - Guarantees idempotent per-`sub` workspace bootstrap and preserves read, export, and electronic signature capabilities when trials expire (`403 K.Subscription.Expired`).
- **Cons:**
  - Requires Keycloak Admin REST synchronization and an immediate client token refresh after onboarding.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Separating `Subscription` and `ProductEntitlement` from `Tenant` and `Grant` enables self-service trial evaluation and in-place tier upgrades (`Individual`, `Select`, `Complete`) while preserving every Clinical Kernel invariant: fail-closed tenant resolution, PostgreSQL RLS, pseudonymous `Person` records, grant-backed clinical visibility, and permanent access to regulated reads, exports, and 21 CFR Part 11 audit/signature primitives.

## 5. Consequences & Trade-offs

- **Positive Impact:** Prospective evaluators can compare modular packaging on `/plans`, self-register through Keycloak PKCE `S256`, and provision an isolated, RLS-protected kernel trial workspace without manual database seeding, while expired trials retain read, export, and electronic signature access.
- **Negative Impact / Technical Debt:** Onboarding depends on Keycloak Admin REST reachability when `KeycloakAdmin:BaseUrl` is configured so updated `tenant_id` and realm role claims appear on the subsequent token refresh.
- **Mitigation Strategy:** Deterministic UUIDv8 identifiers and step-level idempotency keys allow safe retries of `POST /api/v1/kernel/onboarding/trial` if transient network errors interrupt Keycloak writeback without duplicating kernel entities.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/kernel` (`Abstractions`, `Domain`, `Application`, `Persistence`, `Api`), `apps/kernel-api`, `apps/apphost`, `apps/web`, `docker/cadence-realm.json`, `docker/docker-compose.yml`.
- **Verification Plan:** Verified via `CommercialAndOnboardingUnitTests`, `KernelDbContextModelTests`, `KeycloakRealmTenantTests`, PostgreSQL 18 forced-RLS integration tests (`KernelTrialOnboardingPostgresTests`, `KernelSchemaPostgresTests`), and Vitest suites (`trial_onboarding_and_plans.test.js`, `screen_availability.test.js`).
