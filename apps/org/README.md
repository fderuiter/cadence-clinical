# Cadence.Modules.Org (`apps/org`)

Organization Administration module (`org`) hosted in `apps/kernel-api` ([ADR-2610](../../docs/adr/2026-10-15-scaffold-services-become-modules-merge-or-are-deleted.md), [ADR-2611](../../docs/adr/2026-11-20-org-module-oidc-provisioning-and-grant-invitations.md)).

## Architecture

- **Assembly**: `apps/org/src/Cadence.Modules.Org.csproj` (`Cadence.Modules.Org`), registered in `apps/kernel-api` via `AddCadenceOrgModule` and `MapOrgModule()`.
- **No separate database or `DbContext`**: The retired `Cadence.Apps.Org` scaffold and `OrgDbContext` have been replaced by kernel-native `Role`, `Grant`, `Person`, `TenantMembership`, and `IdentityVault` commands (`K-XS-2`).
- **OIDC Provisioning**: `IOidcUserProvisioner` (`KeycloakOidcUserProvisioner` in live stacks, `InMemoryOidcUserProvisioner` in `Development`/`Testing` when no live Keycloak Admin credentials are configured) provisions or reuses Keycloak 26 realm users, binds `PersonId == OIDC sub`, synchronizes `tenant_id`, `study_ids`, and `site_id` user attributes, assigns realm roles, and triggers `UPDATE_PASSWORD` welcome emails without logging PII (`ADR-2458`).
- **Pseudonymous Reads & Audited Disclosure**: `GET /api/v1/org/team` returns pseudonymous member handles (`Member <shortId>`) without triggering `IdentityVault` disclosure audit entries (`K-IDN-1`). Authorized callers (`identity.reveal.person`) disclose individual colleague identities on demand via `POST /api/v1/org/members/{personId}/reveal` with a signed `X-Change-Reason` and `Idempotency-Key`.

## Endpoints (`/api/v1/org`, routed by Gateway to `kernel-api`)

- `GET /api/v1/org/team` — Pseudonymous tenant directory, roles, assignable scopes, delegable grants, and clinical duty catalog.
- `POST /api/v1/org/invitations` — Provisions or reuses OIDC user, enrolls pseudonymous person & tenant membership, seals PII in `IdentityVault`, and issues a kernel `Grant` (`IssueGrant`).
- `POST /api/v1/org/delegations` — Provisions or reuses colleague OIDC user and signs bounded child `DelegateGrant` records under an active `WithGrantOption` parent grant, returning RFC 9457 `explanation` payloads on refusal (`K-EXP-1`).
- `POST /api/v1/org/grants/{grantId}/revoke` — Ends a grant or delegation bitemporally (`RevokeGrant`) while preserving prior `kernel.grant_periods` and `kernel.audit_entries`.
- `POST /api/v1/org/members/{personId}/reveal` — Audited `IdentityVault` disclosure of a colleague's name and email (`RevealPersonIdentity`).
