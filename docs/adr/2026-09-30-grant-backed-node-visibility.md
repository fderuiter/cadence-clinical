# ADR-2457: Grant-Backed Node Visibility

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-004 (site users see only their own site's participants; kernel reads mask the rest)

---

## 1. Context & Problem Statement

Every kernel read that returns other objects masks what the reader may not see. That covers lineage, dependents,
navigation, search, the activity stream and simulation. It asks the host's `INodeVisibility` (ADR-2471), and the
kernel registers none, so a host must decide. The kernel API host registered a deny-all visibility until visibility
could be computed from the authority model. The Canvas's Trace and Impact views therefore showed only masked entries.

ADR-2455 put grants, roles and delegations in the kernel. This ADR decides how a reader's grants decide what it
sees. Blinding (K-AUTH-4) and the identity layer (#4782) are separate conditions and are not decided here.

## 2. Decision Drivers & Constraints

- **Spec §9.3:** authority is computed from grants over the scope path `tenant / organization / study / country /
  study_site`. Visibility must use the same grants and paths, not a second model.
- **K-AUTH-3:** a delegation counts only while every grant above it is in effect.
- **ADR-2471:** masked objects stay in the result, so a trace or count never depends on the reader.
- **Least surprise for site staff:** people working at a study site need the study and its protocol versions. They
  must not see other sites' participants.

## 3. Options Considered

1. **A read capability per kind (`enrollment.read`, …).** Precise, but every grant would need a parallel read
   grant. Nothing issues them today, so the Canvas would stay empty.
2. **Any grant in effect covers what its scope covers, and what is above it (chosen).** Authority to act at a scope
   includes seeing it. Read-only people get a grant of a read capability at their scope. Narrower read tokens can
   later restrict this without changing the contract.
3. **Visibility from organization membership alone.** Ignores studies and sites, so a site user would see every
   study of the sponsor.

## 4. Decision Outcome

`GrantNodeVisibility` implements `INodeVisibility`. A host registers it with `AddKernelGrantVisibility()`. For a
reader it takes the grants the reader holds that are in effect now, each together with every grant above it. The
delegation-chain check is shared with `AuthorityEvaluator`. An object is visible when one of these holds:

- A held scope covers the object's scope path.
- The object is **at or above** a held scope on the scope path: the organization, study or country of that scope. A
  study-site grant therefore shows the study, its sponsor, its protocol versions and its country effectivities, and
  not another site of the study.
- The object is a participant or a site, and a held scope covers one of the study sites it reaches: the participant's
  enrollments, or the site's study links. Kernel persistence answers this through `IAuthorityStore.StudySitePathsAsync`.
- The object is tenant reference data, a role or one of the tenant's people (`tenant`, `person`,
  `tenant_membership`, `role`, code systems and their versions, concepts, concept mappings, standard versions and
  library items). Any grant in effect in the tenant is enough for these.
- A tenant-wide grant shows everything.

Everything else is masked `NotPermitted`. A reader with no grant in effect sees nothing, reference data included. An
object the kernel places on no scope path is visible only through a tenant-wide grant, so a new kind is hidden until
it is given a path. Observations and attestations have no path yet.

## 5. Consequences & Trade-offs

- The Canvas and the kernel API show real objects to people who hold grants. The web thread swaps the host's deny-all
  visibility for `AddKernelGrantVisibility()`.
- Visibility is decided now, in both times. A lineage read as of a past time still masks by today's grants, which is
  what the reader may see today.
- Distinct unversioned objects and held/requester grant scopes are resolved together in one batched `PathsOfAsync`
  call (1 set-based query per present kind) over a single connection lease, with uncovered participants and sites
  batched in one `StudySitePathsAsync` call.
- Blinded content (`Blinded`) and identity-layer content (`IdentityVault`) are not masked yet. The kernel holds no
  arm assignment or identity content today; those conditions arrive with the RTSM assertions and #4782.

## 6. Implementation & Verification

- Code:
  - `Cadence.Kernel.Application/Authorization/GrantNodeVisibility.cs` holds the rule and `AddKernelGrantVisibility`.
  - `Cadence.Kernel.Application/Authorization/AuthorityEvaluator.cs` holds `IAuthorityStore` (`PathsOfAsync`,
    `StudySitePathsAsync`, `FindGrantsAsync`) and the shared `GrantChain`.
  - `Cadence.Kernel.Persistence/Authorization/KernelAuthorityStore.cs` implements batched per-kind scope paths,
    parent grant lookups, and study-site paths of participants and sites over shared connection leases.
- Tests:
  - `tests/Cadence.UnitTests/Kernel/GrantNodeVisibilityTests.cs` covers site, country, study, organization and tenant
    grants, the objects above a scope, grants not yet in effect or revoked, and a delegation under a revoked grant.
  - `tests/Cadence.IntegrationTests/Kernel/KernelGrantVisibilityPostgresTests.cs` runs the rule over real rows:
    studies, study sites, sites, participants, enrollments, protocol versions and effectivities.
