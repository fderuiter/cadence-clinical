# Cadence.Modules.Org (`apps/org`)

In-process Clinical Kernel composition module (`ADR-2610`, `ADR-2819`) serving workspace team administration, OIDC user provisioning, encrypted `IdentityVault` person onboarding, bounded authority delegations, and grant revocation under `/api/v1/org`.

## Responsibilities

- **Team Directory (`GET /api/v1/org/team`)**: Projects tenant members, role definitions, active/historical `Grant` timelines, and available scopes via `ITeamDirectoryReader` without storing plaintext PII outside `IdentityVault`.
- **Seat Invitations (`POST /api/v1/org/invitations`)**: Verifies caller `grant.issue` authority before provisioning or reusing a tenant-bound Keycloak OIDC account, dispatching welcome password-setup emails (`UPDATE_PASSWORD`), registering kernel `TenantMembership`, sealing direct identifiers in `IdentityVault`, and issuing `IssueGrant`.
- **Bounded Delegations (`POST /api/v1/org/delegations`)**: Validates parent grant scope, capabilities, `WithGrantOption`, and validity bounds before provisioning/reusing the delegatee's OIDC identity and issuing `DelegateGrant`, returning structured kernel `Explanation` payloads on refusal.
- **Revocation (`POST /api/v1/org/grants/{grantId}/revoke`)**: Executes `RevokeGrant` with gateway-signed `X-Change-Reason`, ending downstream access and child delegations while preserving bitemporal `grant_periods` history and immutable audit records.
