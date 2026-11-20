# ADR-2456: Effectivity and Study-Site Activation

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CTMS-005 (site activation gated on approvals), PRD-SYS-001 (audit entries for every
  effectivity change and activation)

---

## 1. Context & Problem Statement

Spec §7.1 separates approval from effectiveness: an approved protocol version is usable at a site only after the
approvals of that jurisdiction. **K-TIME-4** says a study site is activated only when a version is effective there,
and **K-TIME-5** says an effectivity becomes effective only on approval evidence. The inventory (§3 item 6) had
`apps/execution` gate activation on a site compliance cache that nothing kept in step with the approvals.

The kernel had approved protocol versions (ADR-2452), evidence artifacts (ADR-2454) and study sites, but no record of
where a version was in force and nothing that stopped a study site from being activated. This ADR decides how
effectivity is stored, how it is resolved at a study site, and how activation checks it. Consent status and
applicability, the rest of #4777, are later slices.

## 2. Decision Drivers & Constraints

- **K-TIME-1 / ADR-2246:** effectivity is bitemporal. What was in force at a site must be answerable as of any past
  `(valid, known)` pair, and a late-recorded approval must not rewrite what was known before.
- **K-TIME-5:** the evidence must be of an accepted kind and must not change after the decision rests on it.
- **Spec §7.1:** scopes resolve most-specific-first, `StudySite → Country → Study`.
- **Spec §9.3:** effectivity commands are authorized against the study, country or study site they affect.

## 3. Options Considered

### How effectivity is stored

1. **A status column on the protocol version per country.** Cannot express site-level exceptions or history.
2. **An aggregate per version and scope with bitemporal status rows (chosen).** The same pattern as enrollment
   (ADR-2453): the aggregate row holds the current status for fast reads, and `effectivity_status_periods` holds
   the history under the bitemporal guards.

### What counts as evidence

1. **`ArtifactId[]`, as the spec's sketch has it.** A later version of the artifact would silently become the
   evidence of a past decision.
2. **`ArtifactVersionId[]` (chosen).** A version is immutable and hash-addressed, so the evidence of each status row
   is fixed. The artifact's kind and study are checked when the status changes.

### How the scopes combine

1. **Any effective scope wins.** A site-level withdrawal could not override a study-wide effectivity.
2. **The most specific declared scope decides, per version (chosen).** Among the version's effectivities that cover
   the study site and were declared at the instant, the one with the narrowest scope decides, whatever its status.
   A version is in force when that one is `Effective`.

## 4. Decision Outcome

- **Model.** `Effectivity(id, tenant, study, protocol_version, scope_level, scope_country?, scope_study_site?,
status, status_since)` with `EffectivityStatusPeriod(effectivity, status, evidence uuid[], valid_period,
recorded_period)` rows. A version has at most one effectivity per scope.
- **Lifecycle.** `Pending → RegulatoryApproved | EthicsApproved → Effective`, in either approval order and with
  either approval optional. `Withdrawn` is reachable from every status and is terminal, and `Effective` is left only by
  withdrawal. A change takes effect after the current status began and not after it is recorded.
- **Evidence.** `EthicsApproved` accepts an ethics approval. `RegulatoryApproved` accepts a regulatory approval or a
  CTA acknowledgement. `Effective` needs at least one of the three and an approved (or superseded) version.
  `Pending` and `Withdrawn` take none. Each item must be a version of an artifact of the effectivity's study or a
  tenant-wide one. At most 16 items, each named once.
- **Commands.**
  - `DeclareEffectivity` (`effectivity.declare`, Execute, authorized against the protocol version).
  - `ChangeEffectivityStatus` (`effectivity.change_status`, Approve, authorized against the effectivity, whose scope
    path is its study, study and country, or study site).
  - `ActivateStudySite` (`study_site.activate`, Approve, authorized against the study site).
  - They raise `EffectivityDeclared`, `EffectivityStatusChanged` and `StudySiteActivated` (Execution family).
- **Resolution.** `EffectivityResolution.EffectiveAt` applies §7.1 as in option 2 above. `IEffectivityReader`
  exposes it to applications for any study site and `(valid, known)` pair, highest version number first.
- **Activation.** `ActivateStudySite` resolves at the command's recorded time and refuses with
  `K.Effectivity.NoEffectiveVersion` when nothing is in force. The event names the versions that were.
- **Database.** Both tables are tenant-isolated. `effectivities` checks that the scope columns fit the level and has
  a unique index on version and scope with nulls not distinct. Its study-site foreign key includes the study, so a
  study site of another study cannot be named. `effectivity_status_periods` carries the bitemporal guards (`KT001`).
- **Also fixed.** The scope checks of `effectivities` and `grants` (ADR-2455) now require a country for a country
  scope. A null country made the regular expression yield null, which a check constraint accepts. The same
  migration replaces the grants check.

## 5. Consequences & Trade-offs

- eTMF evidence now directly gates site activation, and every activation can be explained from kernel rows: the
  effectivity that decided, its status row and the artifact versions behind it.
- A site-level or country-level effectivity that is not yet effective holds a version out of force there even when it
  is effective study-wide. Sponsors declare narrow scopes only where a local decision differs.
- The aggregate row's current status is a convenience for reads. The history rows are authoritative and append-only;
  the aggregate row itself has no database guard beyond its checks and keys.
- The other start-up conditions of §7.1 (contracts executed, essential documents present, site initiation) and the
  authority evaluator's StudySite condition (§9.3 step 3) are not yet checked.

## 6. Implementation & Verification

- Code:
  - `Cadence.Kernel.Abstractions`: `EffectivityId`, `Execution/Effectivity.cs` (status, scope, transitions,
    `IEffectivityReader`), `Commands/EffectivityCommands.cs`, `Events/EffectivityEvents.cs`.
  - `Cadence.Kernel.Domain/Entities/Effectivity.cs` (aggregate, status rows and resolution).
  - `Cadence.Kernel.Application/Execution/EffectivityCommandHandlers.cs`.
  - `Cadence.Kernel.Persistence`: `Configurations/EffectivityConfigurations.cs`, `Execution/KernelEffectivityReader.cs`,
    the effectivity scope path in `Authorization/KernelAuthorityStore.cs`, and migration
    `EffectivityAndStudySiteActivation`.
- Tests:
  - `tests/Cadence.UnitTests/Kernel/EffectivityTests.cs` covers scopes, every transition, the evidence kinds, the
    bitemporal history and resolution, including the spec's site A / site B example.
  - `tests/Cadence.IntegrationTests/Kernel/KernelEffectivityPostgresTests.cs` covers activation gated on an effective
    version (PRD-CTMS-005), the evidence and declaration rules, the reader as of past times, and the database guards.
