# ADR-2455: Grants, Roles and Delegation as Bitemporal Kernel Authority

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001 (audit entries for every authority change and every denial)

---

## 1. Context & Problem Statement

Spec §9.3 computes authority from evidence: an actor holds **Grants** of capabilities at a scope for an effective
period, a **Role** is only a named bundle of capabilities, and a **Delegation** is a grant issued by someone who
holds the capability with the grant option, bounded by it (the site delegation-of-authority log). ADR-2251 chose a
hand-coded evaluator behind `IAuthorizer`, and ADR-2483 gave the kernel one decision procedure
(`IAuthorizationEvaluator`) for commands and dry runs. The procedure had no evidence to read: no grant, role or
delegation existed in the kernel, so every host had to plug in its own evaluator.

This ADR decides how grants, roles and delegations are stored and changed, how the evaluator reads them, and how
the first grants in a new tenant come to exist. Qualifications (K-AUTH-5) and separation of duties (§9.3 step 4)
are later slices of #4781.

## 2. Decision Drivers & Constraints

- **K-AUTH-3:** an activity outside an active delegation is denied, and a retroactive delegation cannot make it
  valid. What a grant allowed must be answerable as of any past `(at, known_at)`.
- **K-AUTH-2 / K-AUTH-6:** permits and denials are recorded with the grants that justified them, and a denial says
  what is missing.
- **K-AUTH-4:** unblinding capabilities are never bundled by default.
- **K-TIME-1:** effective periods are bitemporal (ADR-2246); nothing is updated in place.
- **Spec §11:** automation agents read and propose; a person executes and approves.
- **Bootstrap:** a new tenant has no grants, yet someone must issue the first ones without a back door that
  bypasses the pipeline.

## 3. Options Considered

### Where authority lives

1. **Role strings in the identity token (status quo in `packages/security`).** Cannot express scope, effective
   dates, delegation or history, and the kernel cannot explain a decision from them.
2. **Kernel grants (chosen).** A `Grant` names grantee, one capability or one role, a scope and a bitemporal
   validity. Roles are immutable bundles.

### How delegation works

1. **A separate `Delegation` table.** Duplicates grant semantics and needs a second evaluation path.
2. **A delegation is a grant with a parent (chosen).** `DelegateGrant` issues a grant whose `parent_grant_id`
   names the delegating grant. It must fit inside the parent: capabilities, scope and validity. The evaluator
   requires the whole chain to be in effect, so revoking a grant ends every delegation under it without touching
   their rows.

### How revocation changes validity

1. **Delete or update the grant.** Loses what was true before, breaking K-AUTH-3.
2. **Supersede the validity row (chosen).** `grant_periods` holds bitemporal rows. Revoking at `t` closes the current
   row's system time and, if `t` is after the start, records a new row valid `[start, t)`. A grant revoked
   retroactively is still shown to have been in effect for decisions made before the revocation was recorded.

### Bootstrapping a tenant

1. **A seed migration or direct SQL.** Unaudited and invisible to the chain.
2. **Configured platform operators (chosen).** Service accounts named in `KernelAuthorityOptions.PlatformOperators`
   hold `tenant.provision`, `grant.issue` and `role.define`, and nothing else, without a grant. Everything they do
   goes through the pipeline and the audit chain.

## 4. Decision Outcome

- **Model.** `Role(id, tenant, code, name, capabilities[])` is insert-only. The code matches
  `^[A-Z0-9][A-Z0-9_-]{0,31}$`, capabilities are sorted and distinct, and a role refuses any unblinding capability
  (`Capability.IsUnblinding`, K-AUTH-4). `Grant(id, tenant, grantee, capability | role, scope, with_grant_option,
parent_grant_id?, issued_by)` carries its validity in `GrantPeriod` bitemporal rows.
- **Scope.** `AuthorityScope(level, target?, country?)` has five levels: `Tenant`, `Organization`, `Study`,
  `StudyCountry` and `StudySite`. It covers a resource when the resource's `ScopePath` (organization, study,
  country, study site) matches its target. The kernel computes the path of organizations, sites, studies, study
  sites, enrollments, protocol versions, artifacts and grants. Any other object is tenant-wide, which only
  tenant-level grants cover.
- **Commands.**
  - `DefineRole` (`role.define`, Approve).
  - `IssueGrant` (`grant.issue`, Approve).
  - `DelegateGrant` (`grant.delegate`, Execute, authorized against the parent grant).
  - `RevokeGrant` (`grant.revoke`, Approve, authorized against the grant).
  - They raise `RoleDefined`, `GrantIssued`, `DelegationSigned` and `GrantRevoked` (Trust family).
- **Refusals.** A grant naming an unknown role or scope object is refused. So is a delegation that exceeds its
  parent's capabilities, scope or validity, one made to oneself, or one under a parent without the grant option
  that is not in effect.
- **Evaluation.** `AuthorityEvaluator` implements `IAuthorizationEvaluator`. It applies these rules in order:
  1. An automation agent is denied Execute and Approve.
  2. A platform operator is permitted its bootstrap capabilities.
  3. Only the holder of a grant in effect with the grant option may delegate under it.
  4. The signer of a delegation may revoke it.
  5. Otherwise the actor needs a held grant that carries the capability, directly or through its role, whose
     scope covers the resource and which is in effect at `(at, known_at)` together with every grant above it. The
     chain is at most eight deep.
- **Explanation.** Every decision names the condition it evaluated with its evidence: `grant.active`,
  `grant.delegable`, `grant.delegator`, `actor.platform_operator` or `actor.agent_verb`. The permitting grant is
  named as a NodeRef.
- **Recording (K-AUTH-2).** The pipeline hands the permit to the transaction. `provenance_activities.authorization`
  stores `{permitted, reason, conditions}` for permits and denials alike.
- **Database.** `kernel.roles`, `kernel.grants` and `kernel.grant_periods` are tenant-isolated. `roles` is
  insert-only (`KA001`). `grant_periods` carries the bitemporal guards (`KT001`). `grants` checks that exactly one
  of capability and role is set, and that the scope columns fit the level.
- **Wiring.** A host calls `AddKernelAuthorization()` and `AddKernelAuthorityEvaluator(platformOperators)`.
  Kernel persistence registers the `IAuthorityStore`.

## 5. Consequences & Trade-offs

- A decision is now explainable from kernel rows: which grant permitted it and which delegation chain it rests on.
- Revoking a grant ends every delegation under it at evaluation time. The delegations' own rows stay as they were,
  so the delegation log still shows what was signed.
- Evaluation reads the actor's grants and the parent chain per request. That is acceptable at current volumes, and
  a per-request cache can come later without changing the contract.
- Qualifications, separation of duties, site activation (Effectivity, #4777) and blinding conditions are not yet
  evaluated. Until they are, a grant alone permits.
- Existing product RBAC in `packages/security` keeps gating product endpoints until those products move onto the
  kernel (#4787).

## 6. Implementation & Verification

- Code:
  - `Cadence.Kernel.Abstractions`: `GrantId`, `RoleId`, `Authorization/AuthorityScope`, `Commands/AuthorityCommands`, `Events/AuthorityEvents`, `Capability.IsUnblinding`.
  - `Cadence.Kernel.Domain/Entities/Role.cs` and `Grant.cs`.
  - `Cadence.Kernel.Application/Authorization/AuthorityEvaluator.cs` and `Trust/AuthorityCommandHandlers.cs`.
  - `Cadence.Kernel.Persistence`: `Configurations/AuthorityConfigurations.cs`, `Authorization/KernelAuthorityStore.cs`, and migration `GrantsRolesAndDelegation`.
- Tests:
  - `tests/Cadence.UnitTests/Kernel/AuthorityTests.cs` covers the domain rules, scope covering and every evaluator rule.
  - `tests/Cadence.IntegrationTests/Kernel/KernelAuthorityPostgresTests.cs` covers three things. The first is the spec's scenario 3: an act outside a delegation is denied and audited, and a retroactive delegation does not validate it. The second is the delegation bounds. The third is the database guards.
