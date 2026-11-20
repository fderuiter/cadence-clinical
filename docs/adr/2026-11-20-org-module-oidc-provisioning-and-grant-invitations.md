# ADR-2611: Org Module OIDC Provisioning, Seat Invitations, and Delegated Grants

- **Status:** Accepted
- **Date:** 2026-11-20
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001, PRD-SYS-004, PRD-SYS-191, PRD-SYS-192

---

## 1. Context & Problem Statement

Under [ADR-2610](2026-10-15-scaffold-services-become-modules-merge-or-are-deleted.md), `apps/org` (`Cadence.Apps.Org` on port `8012`) was an unimplemented scaffold with an empty `Program.cs` and a local `OrgDbContext` that duplicated kernel concepts (`DelegationOfAuthority`, `PersonnelAssignment`, `TrainingLog`, `OrgAuditLog`). Meanwhile, after self-registering and bootstrapping a trial workspace ([ADR-2547](2026-11-20-keycloak-self-registration-and-clinical-kernel-trial-bootstrap.md)), Sponsor Admins and Principal Investigators had no self-service path to invite teammates by email, provision OIDC accounts with welcome links, seal invitee identities into `IdentityVault`, issue scoped `Grant` records, or delegate site duties without manual Keycloak administration or raw subject identifiers.

## 2. Decision Drivers & Constraints

- **Single Source of Clinical Authority (K-XS-1, K-XS-2, ADR-2600):** All personnel authority, delegation chains, and revocations must live exclusively in kernel `Role` and bitemporal `Grant` records (`kernel.grants` and `kernel.grant_periods`), mutated only via `IKernelCommandSender`.
- **Pseudonymous Clinical Core & Zero PII Leakage (ADR-2458, K-ID-4):** `kernel.people`, `kernel.tenant_memberships`, `kernel.grants`, `kernel.audit_entries`, and `GET /api/v1/org/team` must store and return only pseudonymous identifiers (`PersonId`, `Actor`, `GrantId`, `NodeRef`). Human names and email addresses live exclusively in `IdentityVault` (`identity_vault` schema, sealed via AES-256-GCM + HMAC-SHA256 blind index) and the external OIDC identity provider; unsealing requires an explicit audited `RevealIdentity` (`POST /api/v1/org/members/{personId}/reveal`) write.
- **Bounded Delegation with Structured Refusal Explanations (K-EXP-1, ADR-2455):** Sub-delegations must never exceed the delegator's active `WithGrantOption` parent grant across capabilities, scope graph lineage, or validity window, and refusals must surface structured RFC 9457 `explanation` payloads (`claim`, `conditions` with `met`, `next_actions`) verbatim to the UI.
- **Modular Monolith Hosting (ADR-2601, ADR-2610):** Retire the standalone `Cadence.Apps.Org` container (`8012`, `ORG_SERVICE_URL`) and compose `Cadence.Modules.Org` in-process inside `Cadence.Kernel.Host` (`8018`) under `/api/v1/org/**`.

## 3. Options Considered

### Option 1: Extend Standalone `Cadence.Apps.Org` and `OrgDbContext`

- **Overview:** Implement invitation and delegation tables inside `OrgDbContext` on port `8012` and synchronize grants asynchronously with `kernel-api`.
- **Pros:**
  - Keeps `apps/org` isolated in its own process.
- **Cons:**
  - Violates `ADR-2610`, `K-XS-1`, and `K-MOD-1` by maintaining duplicate authority and audit tables outside the kernel.
  - Introduces split-brain windows between local DOA rows and `AuthorityEvaluator` decisions.

### Option 2: In-Process `Cadence.Modules.Org` Over `IdentityVault`, `IOidcUserProvisioner`, and Kernel `Grant` Commands (Selected)

- **Overview:** Convert `apps/org/src` into class library `Cadence.Modules.Org` hosted by `Cadence.Kernel.Host` (`/api/v1/org/**`); retire `OrgDbContext` and port `8012`; add blind-index lookup `find_subject_by_blind_index(text, bytea)` to `IdentityVault` so repeat invitations reuse existing `PersonId` records deterministically; provision or look up Keycloak 26 users via `IOidcUserProvisioner` (`KeycloakOidcUserProvisioner`, sharing `KeycloakAdmin:*` defaults with `ADR-2547` and triggering `execute-actions-email` `UPDATE_PASSWORD` / `VERIFY_EMAIL`); enroll `Person` + `TenantMembership` + `RecordPersonIdentity` + `IssueGrant` through `IKernelCommandSender`; bridge CTMS `/doa/delegate` and `/doa/{id}/revoke` to `/api/v1/org/delegations` and `/api/v1/org/grants/{id}/revoke`; and expose `/team` (`TeamManagementView.vue`) in the Clinical Canvas.
- **Pros:**
  - Eliminates `OrgDbContext` and 13 `K-XS-1`/`K-MOD-1` architecture baseline violations.
  - Enforces immediate bitemporal revocation across `CanI`, `Search`, and Canvas reads while preserving `kernel.grant_periods` and `kernel.audit_entries` for 21 CFR Part 11 inspection.
  - Preserves strict pseudonymity on directory reads (`0` disclosure audits on `GET /api/v1/org/team`) and audits every identity reveal (`POST /api/v1/org/members/{personId}/reveal`).
- **Cons:**
  - Requires Keycloak Admin REST availability when provisioning OIDC accounts in non-test environments.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Hosting `Cadence.Modules.Org` inside `Cadence.Kernel.Host` fulfills `ADR-2610` (#5375) with zero duplicate persistence, unifies seat invitations and site DOA delegations onto kernel `Role`, `Grant`, `Person`, `TenantMembership`, and `IdentityVault` primitives, and guarantees that OIDC identity provisioning, pseudonymity, bounded sub-delegation, and bitemporal revocation obey every Clinical Kernel invariant end-to-end.

## 5. Consequences & Trade-offs

- **Positive Impact:** Sponsor Admins and Principal Investigators can invite teammates and delegate duties by email from `/team` and CTMS DOA logs; invitees receive Keycloak welcome actions and immediately see authorized studies upon sign-in; out-of-bounds delegations render structured kernel refusal explanations; and revocations take effect immediately without deleting historical grant periods or audit chain entries.
- **Negative Impact / Technical Debt:** Cross-tenant OIDC reuse is intentionally refused (`409 Org.CrossTenantConflict`) while a single `tenant_id` attribute backs the realm token mapper (`ADR-2514`).
- **Mitigation Strategy:** Deterministic UUIDv8 `PersonId` derivation and `Idempotency-Key` command envelopes make invitation retries safe across transient OIDC failures, and `InMemoryOidcUserProvisioner` provides deterministic offline test coverage when `KeycloakAdmin:BaseUrl` / `Org:Keycloak:AdminBaseUrl` is unset.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/kernel` (`Abstractions`, `Application`, `Modules`, `Persistence`), `apps/org` (`Cadence.Modules.Org`), `apps/kernel-api`, `apps/ctms`, `packages/topology`, `apps/apphost`, `apps/web`, `docker/docker-compose.yml`, `packages/tooling_core`.
- **Verification Plan:** Verified via `ArchitectureSentinelTests` and `KernelBoundaryTests`, `ServiceTopologyTests`, `GatewayRouteCoverageTests`, `KeycloakOidcUserProvisionerTests`, `DoaKernelDelegationTests`, PostgreSQL 18 integration tests (`OrgModulePostgresTests`, `KernelDevelopmentTenantPostgresTests`, `KernelTrialOnboardingPostgresTests`), and Vitest suites (`team_management.spec.js`, `screen_availability.test.js`).
