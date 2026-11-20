# ADR-2819: In-Process Organization Administration Module for OIDC User Provisioning and Kernel Grant Invitations

- **Status:** Accepted
- **Date:** 2026-11-20
- **Requirements:** PRD-SYS-001, PRD-CRF-013, SRS-SYS-005
- **Supersedes / Amends:** Retires standalone `Cadence.Apps.Org` (`8012`) per ADR-2610

## Context

Self-serve tenant onboarding and clinical site delegation previously split authority across manual Keycloak administration, a standalone `apps/org` directory microservice (`Cadence.Apps.Org`), and CTMS site Delegation of Authority (DOA) logs keyed by raw user UUIDs. Inviting a colleague or delegating site duties could not atomically verify kernel authority, provision a tenant-bound OIDC identity, seal personal identifiers in `IdentityVault` (ADR-2458), and issue bitemporal `Grant` records (ADR-2455).

## Decision

1. **Retire Standalone `org` Service into `Cadence.Modules.Org` (`ADR-2610`)**: Replace `Cadence.Apps.Org` and `OrgDbContext` with the in-process kernel module `Cadence.Modules.Org` hosted inside `Cadence.Kernel.Host` (`apps/kernel-api`) under `/api/v1/org`.
2. **Preflight Kernel Authority Before OIDC Mutation**: Invitation (`POST /api/v1/org/invitations`) and bounded delegation (`POST /api/v1/org/delegations`) verify `grant.issue` or parent `DelegateGrant` bounds (`WithGrantOption`, capability subset, scope coverage, validity window, and `K-AUTH-4` unblinding exclusion) before creating any OIDC user, returning RFC 9457 `ProblemDetails` with structured `Explanation` payloads on refusal.
3. **Tenant-Bound OIDC Provisioning & Blind-Index Reuse**: `IOidcUserProvisioner` (`KeycloakOidcUserProvisioner` in production, `InMemoryOidcUserProvisioner` in local dev/testing without admin credentials) binds OIDC `sub` UUIDs to `PersonId`, sets `tenant_id`, `study_ids`, and `site_id` attributes, dispatches `UPDATE_PASSWORD` execute-actions emails for newly created accounts, reuses existing tenant identities via `IIdentityVault.FindPersonByEmailAsync`, and rejects cross-tenant email collisions with HTTP 409 (`M.Org.Identity.CrossTenantConflict`).
4. **Email-Driven CTMS Site Delegation Bridge**: `apps/ctms` delegates bounded site duties by colleague email via `IKernelDelegationBridge` -> `POST /api/v1/org/delegations`, storing only pseudonymous `PersonId` and linked `KernelGrantIds` in `ctms.ctms_delegations` and revoking linked kernel grants on DOA revocation.

## Alternatives Considered

- **Keep `Cadence.Apps.Org` as a separate microservice**: Rejected because ADR-2610 retires `org` into the kernel composition host, and cross-service sagas between `org` and `kernel` would split authorization, `IdentityVault` sealing, and bitemporal `Grant` issuance across network hops.
- **Store colleague names and email addresses in `org` or `ctms` tables**: Rejected because ADR-2458 mandates encrypting direct personal identifiers exclusively in `IdentityVault` with keyed HMAC blind indexes.

## Consequences

- **Positive:** Single composition-host boundary for OIDC onboarding, `IdentityVault` encryption, `Grant` issuance/delegation/revocation, and immutable tenant audit chain entries with gateway-signed `X-Change-Reason` (ADR-2495).
- **Negative / Trade-offs:** External Keycloak user creation cannot share a two-phase commit with PostgreSQL; provisioning is therefore preflight-checked first and idempotent on retry (`Idempotency-Key` + blind-index / tenant-attribute reconciliation).
