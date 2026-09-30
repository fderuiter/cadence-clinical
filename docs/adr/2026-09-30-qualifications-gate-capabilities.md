# ADR-2459: Qualifications Gate Capabilities

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-TRN-002 (the kernel gate that blocks an action while mandatory protocol training is
  outstanding; the EDC read-only rendering it also asks for is application work, so no test claims it yet)

---

## 1. Context & Problem Statement

Spec §9.3 says a grant is not the whole answer. Step 3 of the decision procedure checks conditions, and the first
of them is that the actor's required Qualifications are current. K-AUTH-5 makes training and credentials conditions
evaluated from Qualifications, so training evidence gates actions directly. K-AUTH-6 says a denial names the missing
evidence, for example "protocol v3 training not current". Spec §14 scenario 12 is the acceptance test: an eligibility
signature by an actor whose protocol training expired is denied, and the denial names the expired qualification.

ADR-2455 implemented grants, roles and delegation (steps 1 and 2) and left qualifications for a later slice of #4781.
Before this decision a grant was enough, and training lived only in application tables the kernel could not see.
This ADR decides how qualifications and the requirements that need them are stored, and how the evaluator applies
them.

## 2. Decision Drivers & Constraints

- **K-AUTH-5:** prerequisites belong to the actor. Site readiness is StudySite and Effectivity (§7.1), not a
  qualification.
- **K-AUTH-6:** a denial says what to obtain or renew, and every decision lists the conditions it checked with their
  evidence NodeRefs.
- **K-AUTH-3:** training recorded late cannot validate an earlier action. The evaluator already answers at
  `(at, known_at)`, so qualifications must be bitemporal like grants.
- **No role strings:** spec §9.3 computes `CanSignEligibility` from evidence. Which qualification a capability needs
  must be data a sponsor can change, not code.
- **Evidence:** a qualification is only as good as the record that proves it. The kernel already stores immutable,
  hash-addressed artifact versions (ADR-2454).
- **K-TIME-1, K-ID-1, K-ID-2:** bitemporal rows are append-only, and every table is tenant-scoped with composite
  foreign keys and row-level security.

## 3. Options Considered

1. **A qualification condition on the grant.** A grant would name the qualification it needs. This ties training to
   each grant, so a new protocol amendment means reissuing every grant, and a delegation would have to copy its
   parent's conditions.
2. **Qualification requirements as their own data (chosen).** A requirement names a capability, a qualification code,
   optionally a protocol version, and a scope. The evaluator applies every requirement in effect over the resource.
   One requirement covers everyone who acts at that scope, however their authority reached them.
3. **Requirements derived from the protocol design.** The protocol would declare the training each activity needs.
   This is where the product should go (PRD-TRN-001 maps amendments to curricula), but it needs the governing
   protocol version of every resource, which the kernel does not resolve yet. Option 2 is the storage that option 3
   will write to.

## 4. Decision Outcome

**Qualifications.** A `Qualification` is held by an actor and says what it attests:

| Kind | Meaning |
| --- | --- |
| `Training` | Completed training, such as GCP or a system's user training |
| `ProtocolTraining` | Training on one protocol version, which it names |
| `Credential` | A professional credential or licence |
| `Certificate` | A certificate, such as a GCP or IATA certificate |

It has a lower snake_case `code` (at most 64 characters) that requirements match. Only `ProtocolTraining` names a
protocol version, and it must. Its evidence is a version of an artifact of kind `TrainingRecord` or `Credential`, two
new artifact kinds. Its validity is bitemporal in `qualification_periods`, exactly like a grant's: revoking it closes
the recorded period of the current row and records the shortened validity.

**Requirements.** A `QualificationRequirement` says that exercising a capability within a scope needs a current
qualification of a code, and of a protocol version when it names one. Its validity is bitemporal too, so a decision
re-evaluated as of an earlier instant applies the requirements known then.

**Commands.** Each is a kernel command, authorized, audited and raising a Trust event:

| Command | Capability | Verb class | Authorized against | Event |
| --- | --- | --- | --- | --- |
| `RecordQualification` | `qualification.record` | Execute | tenant | `QualificationRecorded` |
| `RevokeQualification` | `qualification.revoke` | Approve | the qualification | `QualificationRevoked` |
| `DefineQualificationRequirement` | `qualification_requirement.define` | Approve | tenant | `QualificationRequirementDefined` |
| `RetireQualificationRequirement` | `qualification_requirement.retire` | Approve | the requirement | `QualificationRequirementRetired` |

Recording is Execute because filing a training record is routine work, and automation agents cannot do it. Revoking
and changing requirements are Approve because they change who may act.

**Evaluation.** After a grant permits (steps 1 and 2), the evaluator loads the requirements for the capability, keeps
those in effect at `(at, known_at)` whose scope covers the resource's scope path, and checks each against the
qualifications the actor holds in effect at the same instant. Each requirement adds one `qualification.current`
condition:

- Met: "Holds current gcp", with the requirement, the qualification and its evidence artifact version.
- Unmet with a matching qualification that lapsed: "Renew protocol_training for protocol version …; it expired on
  2026-09-28", with the requirement and the lapsed qualification.
- Unmet otherwise: "Obtain gcp", with the requirement.

If any condition is unmet the decision is a denial, "`<actor>` is not qualified to `<capability>`: `<first unmet>`".
The `DecisionDenied` event and the audit entry carry every condition, met and unmet (K-AUTH-2, K-AUTH-6).

**Storage.** Four tables: `qualifications`, `qualification_periods`, `qualification_requirements` and
`qualification_requirement_periods`. All four are tenant-isolated, and both period tables carry the K-TIME-1 and
K-TIME-2 guards. Check constraints keep the code pattern, tie `protocol_version_id` to `ProtocolTraining`, and keep a
requirement's scope well formed. Composite foreign keys tie a qualification to its evidence artifact version and
protocol version, and a requirement to its protocol version, in the same tenant.

**Visibility.** A requirement is placed on its scope path, like a grant. A qualification is placed on no path: it is
evidence about a person across studies, so only a tenant-wide grant sees it (ADR-2457).

## 5. Consequences & Trade-offs

- Spec §14 scenario 12 passes against PostgreSQL. Training evidence now gates actions without any application table.
- A requirement adds two reads to a permitted decision only when a requirement exists for the capability. The
  requirements are looked up by `(tenant_id, capability)` and the qualifications by holder, both indexed.
- The evaluator does not work out which protocol version governs a resource. A requirement names the version
  explicitly, so a sponsor defines a new requirement (and retires the old one) when an amendment is approved.
  Deriving requirements from the governing protocol version is follow-up work with the protocol-training mapping of
  PRD-TRN-001.
- Qualifications are held by actors, not by persons across tenants. A person's GCP certificate is recorded in each
  tenant that relies on it, because the evidence artifact is the tenant's own.
- Only the first unmet condition is in the denial's reason; the decision lists all of them. A caller that needs
  every missing qualification reads the conditions.
- Artifact versions now have a foreign key from `qualifications`, so a plain `TRUNCATE` of `artifact_versions` stops at
  the foreign key before the insert-only guard. The guard still refuses `TRUNCATE … CASCADE`, and its test uses that.
- Separation of duties (step 4) and the StudySite, protocol and blinding conditions of step 3 remain for later slices
  of #4781.

## 6. Implementation & Verification

- Code:
  - `Cadence.Kernel.Abstractions/Commands/QualificationCommands.cs`, `Events/QualificationEvents.cs`,
    `QualificationId` and `QualificationRequirementId` hold the contract; `ArtifactKind` gains `TrainingRecord` and
    `Credential`.
  - `Cadence.Kernel.Domain/Entities/Qualification.cs` and `QualificationRequirement.cs` hold the aggregates and their
    periods.
  - `Cadence.Kernel.Application/Trust/QualificationCommandHandlers.cs` holds the handlers.
    `Authorization/AuthorityEvaluator.cs` applies the requirements and names what is missing.
  - `Cadence.Kernel.Persistence`: `Configurations/AuthorityConfigurations.cs`, `KernelAuthorityStore` and migration
    `20260930133015_Qualifications`.
- Tests:
  - `tests/Cadence.UnitTests/Kernel/AuthorityTests.cs` covers validation, bitemporal revocation, requirement
    matching, denial wording for a missing, expired or wrong-version qualification, scope and capability filtering,
    and training recorded late.
  - `tests/Cadence.IntegrationTests/Kernel/KernelAuthorityPostgresTests.cs` runs spec §14 scenario 12, gates a kernel
    command through the pipeline with the denial and the permit audited, and checks the handler refusals and the
    database guards.
