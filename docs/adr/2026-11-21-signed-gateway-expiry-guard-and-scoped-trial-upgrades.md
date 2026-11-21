# ADR-2604: Signed Gateway Expiry Guard and Scoped Trial Upgrades

- **Status:** Accepted
- **Date:** 2026-11-21
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001, PRD-SYS-004, PRD-SYS-053, PRD-SYS-192

---

## 1. Context & Problem Statement

Following `ADR-2547`, self-registered workspaces provisioned a 30-day `Trialing` subscription in the Clinical Kernel, and `KernelCommandPipeline` refused non-baseline kernel mutations with `403 K.Subscription.Expired` once a trial expired. However, three commercial and cross-service gaps remained:

1. **Standalone Clinical Service Bypass:** The seven standalone clinical hosts (`execution`, `ctms`, `etmf`, `econsent`, `safety`, `payments`, `tickets`) did not receive tenant commercial read-only state in the gateway's signed identity headers, allowing expired workspaces to mutate standalone clinical records.
2. **Unscoped Reactivation & Countdown Reset:** Switching commercial tiers (`Individual`, `Select`, `Complete`) shared a single command with paid activation, immediately promoting workspaces to `Active`, clearing `TrialEndsAt`, and allowing expired workspaces to reactivate without structured study scoping or purchase-order/quote confirmation.
3. **Web Expiry Drift:** Authenticated web views (`AppShell.vue`, `OverviewView.vue`, `PlansView.vue`) neither displayed exact trial countdown days nor synchronized read-and-export workspace state when mutations failed with `403 K.Subscription.Expired`.

## 2. Decision Drivers & Constraints

- **Tamper-Proof Edge Propagation (`PRD-SYS-053`, `ADR-2256`):** Client-supplied commercial headers must never be trusted. Any `X-Tenant-Read-Only` flag forwarded to downstream hosts must be stripped from inbound requests, resolved at the gateway, bound into the canonical HMAC-SHA256 signature (`HmacSigner`), and verified fail-closed by `GatewayAuthenticationHandler`.
- **Regulated Read, Export, and Signature Continuity (`PRD-SYS-001`, `docs/strategy/PRODUCT_CATALOG.md`):** Expired workspaces enter a read-and-export posture rather than a hard lockout: `GET`/`HEAD` reads, regulated archive exports (`POST /api/v1/etmf/documents/export` with `etmf_document:export`), 21 CFR Part 11 step-up electronic signatures (`ElectronicSignatureRequired`), and explicit commercial activation/onboarding endpoints (`AllowExpiredTenantWriteMetadata`) must remain available.
- **Scoped Commercial Activation Without Live Card Processing:** Transitioning a `Trialing` or `Expired` workspace to `Active` requires validated study scoping (`study_phase`, `study_duration_months`, `countries`, `site_count`, `participant_count`, `active_study_count`) and a purchase order or signed quote reference (`billing_confirmation`), returning `422 K.Commercial.ScopingInvalid` with RFC 9457 field errors when incomplete.
- **Strict Commercial vs. Clinical Authority Separation (`PRD-SYS-004`, `PRD-SYS-192`, `ADR-2547`):** Commercial entitlements govern workspace packaging and trial mutation posture only; they never confer study/site visibility or bypass `Grant`, `ClinicalScopeGuard`, blinding, or participant consent.

## 3. Options Considered

### Option 1: Per-Service Direct Database Reads of `kernel.subscriptions`

- **Overview:** Have each standalone clinical service query `kernel.subscriptions` directly on every mutating HTTP request.
- **Pros:**
  - Avoids gateway caching.
- **Cons:**
  - Violates `K-XS-2` by coupling standalone clinical services to kernel commercial persistence tables and adds cross-schema database load on every write.

### Option 2: Gateway-Cached Signed `X-Tenant-Read-Only` Identity, Shared `ExpiredSubscriptionWriteGuardMiddleware`, and Split `SwitchTrialTier` / `ActivateSubscription` Commands (Selected)

- **Overview:** Resolve tenant read-only posture at `apps/gateway` via a 30-second `IClock`-backed `GatewayTenantEntitlementCache` (with immediate response-transform cache eviction after `/api/v1/kernel/entitlements/tier`, `/api/v1/kernel/entitlements/activate`, and `/api/v1/kernel/onboarding/trial`); bind `tenant_read_only` (`X-Tenant-Read-Only`) into `GatewayIdentity` and `HmacSigner`; enforce `ExpiredSubscriptionWriteGuardMiddleware` in `UseCadenceSecurity()` across every service with explicit regulated exemptions; split `SwitchTrialTier` (`POST /api/v1/kernel/entitlements/tier`, active trials only, preserving `Status = Trialing` and original `TrialEndsAt`) from `ActivateSubscription` (`POST /api/v1/kernel/entitlements/activate`, `[BaselineKernelCommand]` requiring validated `SubscriptionScoping`); and surface trial countdowns, scoped intake, and `K.Subscription.Expired` state synchronization in `apps/web`.
- **Pros:**
  - Preserves `K-XS-2` boundaries while enforcing tamper-proof `403 K.Subscription.Expired` write protection uniformly across kernel and standalone hosts.
  - Bounds stale read-only state to at most 30 seconds on natural clock expiry and 0 seconds after in-band tier or activation mutations.
  - Prevents expired tenants from reactivating without study scoping and PO/quote confirmation while keeping active trial countdowns intact across tier evaluations.
- **Cons:**
  - Natural clock-driven trial expiry can lag by up to 30 seconds on standalone hosts until the gateway cache entry expires, though kernel mutations still check `Subscription.IsExpiredAt(clock.UtcNow)` transactionally.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Signing `X-Tenant-Read-Only` at the gateway and enforcing `ExpiredSubscriptionWriteGuardMiddleware` inside `UseCadenceSecurity()` closes the standalone write gap without violating `K-XS-2`, while separating `SwitchTrialTier` from scoped `ActivateSubscription` enforces structured commercial scoping without gating regulated reads, exports, or electronic signatures.

## 5. Consequences & Trade-offs

- **Positive Impact:** Expired workspaces cannot create or mutate clinical records in either `kernel-api` or the seven standalone clinical services (`403 K.Subscription.Expired`), regulated exports and Part 11 signatures remain uninterrupted, trial tier switches preserve the original 30-day window, activation captures auditable study scoping and PO/quote references, and the web shell surfaces exact trial days remaining and upgrade guidance.
- **Negative Impact / Technical Debt:** `kernel.subscriptions` carries seven nullable scoping columns (`study_phase`, `study_duration_months`, `countries_csv`, `site_count`, `participant_count`, `active_study_count`, `billing_confirmation`) populated upon activation.
- **Mitigation Strategy:** `SubscriptionScopingValidator` normalizes and bounds all seven inputs before persistence and emits structured per-field errors (`422 K.Commercial.ScopingInvalid`) consumed directly by `PlansView.vue`.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/security`, `apps/gateway`, `packages/kernel` (`Abstractions`, `Domain`, `Application`, `Persistence`, `Api`), `apps/kernel-api`, `apps/web`, `docs/openapi/kernel_openapi.json`, `docs/schema/kernel.md`.
- **Verification Plan:** Verified via `CadenceSecurityUnitTests`, `GatewayAuthenticationTests`, `ExpiredSubscriptionStandaloneServicesTests` (all 7 standalone hosts), `CommercialAndOnboardingUnitTests`, `Cadence.ArchitectureTests`, PostgreSQL 18 forced-RLS integration tests (`KernelTrialOnboardingPostgresTests`, `KernelSchemaPostgresTests`, `KernelTenantIsolationPostgresTests`), and Vitest/axe-core tests (`trial_onboarding_and_plans.test.js`).
