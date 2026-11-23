# ADR-2605: Signed Gateway Entitlements and Bitemporal Subscriptions

- **Status:** Accepted
- **Date:** 2026-11-22
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001, PRD-SYS-004, PRD-SYS-192

---

## 1. Context & Problem Statement

Following `ADR-2547` and `ADR-2604`, self-service trial onboarding and gateway `X-Tenant-Read-Only` propagation protected expired workspaces from routine standalone writes. However, several commercial, identity, and bitemporal audit boundaries still relied on implicit trust or in-place row mutation:

1. **Identity Provider & Onboarding Privilege Surface:** Keycloak user-profile attributes (`tenant_id`, `study_ids`, `site_id`) were writable outside `ADMIN_EDIT` when unmanaged attributes were enabled, `KeycloakOnboardingClient` fell back to a no-op when `KeycloakAdmin` settings were blank and wrote wildcard `study_ids` (`["*"]`), `POST /api/v1/kernel/onboarding/trial` carried `.AllowExpiredTenantWrite()` and accepted callers already bound to a tenant or clinical role, and trial founder roles included commercial operator capabilities (`subscription.activate`, `subscription.expire`, `entitlement.grant`, `entitlement.revoke`).
2. **Self-Declared Activation & Mutable Commercial History:** `ActivateSubscription` accepted free-text billing confirmations without cryptographic quote verification or `svc:kernel-commercial-billing` authority, `GrantProductEntitlement` and `RevokeProductEntitlement` were marked `[BaselineKernelCommand]` and committed outside the `Subscription` aggregate transaction (`SyncTierProductFamiliesAsync`), `KernelEntitlementEndpoints` synthesized fallback `Idempotency-Key` and `X-Change-Reason` headers, and `Subscription` / `ProductEntitlement` rows updated `CreatedBy`, `ReasonForChange`, `TrialEndsAt`, and `ExpiredAt` in place rather than recording bitemporal `IBitemporalEntity` periods under `BitemporalSql` (`KT001`).
3. **Gateway Posture Leakage & Surface Enforcement Gaps:** `GatewayTenantEntitlementCache` read the user-facing `GET /api/v1/kernel/entitlements` endpoint (exposing `BillingConfirmation`) and failed open when upstream calls errored; downstream identity headers did not sign entitled `ProductFamilies` (`X-Tenant-Product-Families`) or `SubscriptionScoping` caps (`X-Tenant-Scoping`); and `ExpiredSubscriptionWriteGuardMiddleware` exempted every `[ElectronicSignatureRequired]` route rather than only closeout/export actions, allowing non-closeout step-up routes or pre-attestation lock/finding checks to consume single-use tokens or persist orphan `Attestation` rows on expired workspaces.

## 2. Decision Drivers & Constraints

- **Fail-Closed Identity & Commercial Isolation (`PRD-SYS-004`, `PRD-SYS-192`, `ADR-2260`):** `tenant_id`, `study_ids`, and `site_id` must be restricted to `ADMIN_EDIT` in `cadence-realm.json` without wildcard study claims; missing `KeycloakAdmin` configuration must fail closed (`502 K.Commercial.IdentityProviderSyncFailed`); and trial onboarding must reject expired workspaces, callers with existing `tenant_id` claims, foreign `TenantMembership` rows, or clinical roles (`403 K.Commercial.OnboardingCallerInvalid`).
- **Cryptographic Quote Verification Without Live Card Processing:** Paid activation (`ActivateSubscription`) must verify either `CommercialOperators.BillingOperator` (`svc:kernel-commercial-billing`) or an HMAC-SHA256 `SubscriptionQuoteToken` (`qtk1.<payload>.<sig>`) bound to `TenantId`, `Tier`, normalized `SubscriptionScoping`, and unexpired `ExpiresAt`, while keeping external card processors and ERP webhooks out of scope.
- **Append-Only Bitemporal Commercial Ledger (`PRD-SYS-001`, `K002`, `ADR-2246`, `ADR-2495`):** Every commercial mutation must supply an explicit `Idempotency-Key` and ADR-2495 `X-Change-Reason`, consolidate `Subscription` and `ProductEntitlement` writes into one `KernelCommandPipeline` transaction, and persist non-overlapping `tstzrange` periods guarded by `BitemporalSql` (`KT001` immutability triggers and GiST exclusion constraints) so historical `CreatedBy`, `ReasonForChange`, `TrialEndsAt`, and `ExpiredAt` values are never overwritten.
- **Least-Privilege Posture Caching & Signed 20-Surface Enforcement (`ADR-2256`):** Gateway posture resolution must call a dedicated `svc:gateway-entitlements` route (`GET /api/v1/kernel/entitlements/posture`) that omits `BillingConfirmation` and fails closed with `503` on upstream failures, and must bind `X-Tenant-Product-Families` and `X-Tenant-Scoping` into `HmacSigner` for enforcement across `KernelCommandPipeline`, `ProductFamilyGuardMiddleware`, `SubscriptionScopingGuardMiddleware`, `StandaloneTenantSaveChangesInterceptor`, and cross-service publishers/consumers.

## 3. Options Considered

### Option 1: Keep Mutable `Subscription` Rows with Separate Audit Tables and Per-Service Kernel Queries

- **Overview:** Retain single-row `kernel.subscriptions` updates, log changes only in `audit_records`, and let standalone hosts query kernel commercial endpoints on demand.
- **Pros:**
  - Smaller schema change in `KernelDbContext`.
- **Cons:**
  - Overwrites `CreatedBy`, `ReasonForChange`, `TrialEndsAt`, and `ExpiredAt` on commercial state transitions instead of preserving point-in-time `valid_period` / `recorded_period` rows, and couples standalone clinical services to synchronous kernel round-trips on every write.

### Option 2: Bitemporal `SubscriptionPeriod` & `ProductEntitlement`, Signed `qtk1` Quotes, Fail-Closed Posture Cache, and HMAC-Bound Family/Scoping Headers (Selected)

- **Overview:** Convert `Subscription` (`SubscriptionPeriod`, `kernel.subscription_periods`) and `ProductEntitlement` (`kernel.product_entitlements`) to `IBitemporalEntity` with `BitemporalSql` triggers and GiST exclusion constraints committed inside a single `Subscription` aggregate write set; verify `SubscriptionQuoteToken` (`qtk1`) or `svc:kernel-commercial-billing` on activation; gate `GET /api/v1/kernel/entitlements` on `INodeVisibility` + `TenantMembership` with `BillingConfirmation` masked unless `subscription.activate` is held; add `GET /api/v1/kernel/entitlements/posture` (`svc:gateway-entitlements`) with fail-closed `503` handling in `GatewayTenantEntitlementCache`; sign `X-Tenant-Product-Families` and `X-Tenant-Scoping` in `GatewayIdentity` / `HmacSigner` and enforce them across all 20 `/api/v1/<surface>` surfaces, EF Core standalone interceptors, webhooks, and consumers; and restrict expired-tenant `[ElectronicSignatureRequired]` writes to explicit `[AllowExpiredTenantWrite]` closeout routes while validating lock/subscription preconditions before recording `Attestation` rows.
- **Pros:**
  - Guarantees append-only commercial provenance (`KT001`) and single-transaction tier + entitlement consistency without stale idempotency replay across `Complete -> Select -> Complete`.
  - Prevents self-minted `Active` upgrades, cross-tenant onboarding pivots, `BillingConfirmation` disclosure to the gateway cache, and orphan Part 11 `Attestation` rows or step-up token consumption on expired tenants.
  - Enforces product-family entitlements and study/site/participant/duration/phase/country scoping caps uniformly across all 20 backend surfaces and background integration seams without violating `K-XS-2`.
- **Cons:**
  - Expands the canonical gateway HMAC payload (`GatewayIdentity` / `HmacSigner`) with two signed commercial headers (`X-Tenant-Product-Families`, `X-Tenant-Scoping`), requiring coordinated deployment of gateway and downstream hosts in the monorepo.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Combining `IBitemporalEntity` commercial periods, signed `qtk1` quote verification, fail-closed `503` posture resolution, HMAC-signed product-family and scoping headers, and narrow closeout-only expiry exemptions closes every privilege, audit-immutability, and cross-service enforcement gap within existing kernel and `Cadence.Security` primitives.

## 5. Consequences & Trade-offs

- **Positive Impact:** Keycloak user-profile claims are locked to `ADMIN_EDIT` without wildcard studies; missing IdP config fails closed (`502`); onboarding callers are strictly isolated (`403`); commercial periods and entitlements are immutable bitemporal rows (`KT001`); activation requires cryptographic `qtk1` quotes or `svc:kernel-commercial-billing`; gateway posture omits `BillingConfirmation` and fails closed (`503`); all 20 backend surfaces enforce signed `ProductFamilies` and `SubscriptionScoping`; and expired workspaces reject non-closeout electronic signatures before consuming step-up tokens or writing orphan attestations.
- **Negative Impact / Technical Debt:** Operators activating workspaces outside `svc:kernel-commercial-billing` must supply a valid `qtk1` quote token signed with `CommercialQuote:SigningSecret` (falling back to `Security:GatewaySecret` when unset).
- **Mitigation Strategy:** `CommercialQuoteOptions` documents and binds `CommercialQuote__SigningSecret`, `SubscriptionQuoteToken` validates constant-time HMAC-SHA256 signatures and scoping parity, and `PlansView.vue` surfaces dedicated quote-token input and RFC 9457 error feedback.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `docker/cadence-realm.json`, `packages/security`, `apps/gateway`, `packages/kernel` (`Abstractions`, `Domain`, `Application`, `Persistence`, `Api`), `apps/kernel-api`, `apps/execution`, `apps/ctms`, `apps/etmf`, `apps/econsent`, `apps/safety`, `apps/payments`, `apps/tickets`, `apps/web`, `docs/openapi/kernel_openapi.json`, `docs/schema/kernel.md`, `docs/api/kernel.md`.
- **Verification Plan:** Verified via `CadenceSecurityUnitTests` (including all 20 backend surfaces, kernel study-build route family mappings for `/standards`, `/sites`, `/study-sites`, `/effectivities`, and `/artifacts`, and non-closeout vs. closeout electronic-signature routes), `CommercialAndOnboardingUnitTests`, `ExpiredSubscriptionStandaloneServicesTests` (covering all 16 non-closeout + 3 closeout electronic-signature routes across standalone and kernel study-build/validation endpoints), `Cadence.ArchitectureTests`, PostgreSQL 18 forced-RLS and `KT001` bitemporal integration tests (`Cadence.IntegrationTests`), and `apps/web` Vitest suites (`trial_onboarding_and_plans.test.js`).
