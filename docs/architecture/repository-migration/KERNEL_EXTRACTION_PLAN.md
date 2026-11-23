# Kernel extraction preparation plan

> [!NOTE] Historical reference for the former repository migration program. See
> the [project plan](../../PROJECT_PLAN.md) for current delivery and
> [ADR-2603](../../adr/2026-11-19-keep-cadence-in-one-repository.md) for the
> source layout decision.

**Status:** plan only, for review. No source moved, deleted or changed. Tracks
C1 to C5 may proceed on preparation branches. C6, the actual extraction, is
blocked until Tracks A and B pass. **Source revision:** `cadence-clinical`
`main` at `cb28d67` (ADR-2602 accepted, inventory merged in #5681, symbol review
in #5683, PostgreSQL failure detection in #5684). **Decision record:**
[ADR-2602](../../adr/2026-10-28-multi-repository-ownership-and-kernel-package-boundaries.md).
It is accepted and governs the SDK shape in this plan. This plan does not reopen
it. **Shared inventory:** [SOURCE_INVENTORY.md](SOURCE_INVENTORY.md). File-level
facts are taken from it and are not repeated here.

Everything marked **unverified** has not been compiled or run for this plan.

## 1. Gating status

Extraction (C6) starts only when the four conditions in
[MIGRATION_PLAN.md](MIGRATION_PLAN.md) and the
[agent execution guide](AGENT_HANDOFF.md) hold.

1. **Complete source and dependency inventory**
   - **State on `cb28d67`:** **Met for the kernel.** Inventory and symbol review
     merged.
   - **Remaining work:** 107 rows are still `needs decision` (92 Execution, 15
     other). Those rows must not be used for kernel slices.
2. **Approved architecture decision and ownership matrix**
   - **State on `cb28d67`:** **Partly met.** ADR-2602 is accepted. The ownership
     matrix amendments in its §5 are approved but not yet applied. Six owner
     decisions are in draft PR #5688 (not reviewed for this plan).
   - **Remaining work:** Apply the matrix amendments. Close ADR-2602 §8 items 1
     to 3 in K1.
3. **Executable verification baseline for transaction, authorization, audit and
   integrity**
   - **State on `cb28d67`:** **Not met.** #5684 makes the `dotnet-tests` job
     fail when a PostgreSQL test does not run. No run has been recorded against
     PostgreSQL with zero skips. GitHub Actions minutes are exhausted (#5685),
     so CI cannot supply that evidence.
   - **Remaining work:** Track A (§3).
4. **Acceptance criteria and rollback strategy**
   - **State on `cb28d67`:** **Drafted here.** Not yet approved.
   - **Remaining work:** Approval by Fred. Rehearsal of rollback (§6).

## 2. Dependency facts for the kernel SDK

From the `.csproj` files on `main`, consistent with ADR-2602 §1:

- `Cadence.Kernel.Abstractions` has no package references.
  `CommandResult<TResult>` already lives here.
- `Cadence.Kernel.Modules` references `Abstractions` and
  `engine_core/Cadence.Core.Domain`. Its NuGet references are FluentValidation,
  `Microsoft.AspNetCore.OpenApi`, EF Core Relational and
  `Npgsql.EntityFrameworkCore.PostgreSQL`.
- `Cadence.Core.Domain` references `Cadence.Security`, which references
  `Cadence.Kernel.Abstractions`. A consumer of the SDK inherits this chain
  transitively.
- `Modules` uses `AggregateRootBase` (`Commands/CommandHandlerContracts.cs`,
  `Persistence/ModuleModelBuilderExtensions.cs`) and `IGxPAuditable`
  (`Persistence/ModuleDbContext.cs`) from `Core.Domain`. These must be moved or
  redefined before the SDK is published (ADR-2602 §8 item 1).
- The ADR records 16 `Core.Domain` entries in the K-XS-1 baseline, in the
  Randomization, Supply and Rules namespaces. Excluding those namespaces from
  the SDK path clears them without moving `Core.Domain` (ADR-2602 §8 item 2).
  **Unverified:** I have not re-counted these.

Baseline counts: 496 entries in `kernel-boundaries.baseline.txt` (353 K-XS-1,
143 K-MOD-1). The 29 uncovered spec rules in `kernel-rule-coverage.baseline.txt`
are carried over from the earlier draft; **unverified** against current `main`.

## 3. Tracks

The coordinator's direction is that Track C is this thread's work. Tracks A and
B are owned elsewhere and are gating only.

- **Track A, verification:** kernel PostgreSQL suite runs with zero skips, with
  results recorded against a revision. Evidence may be local because CI minutes
  are exhausted (#5685).
- **Track B, ownership:** the matrix amendments and the owner decisions in PR
  #5688 are applied, and the `needs decision` rows that K1 uses are resolved.
- **Track C, preparation (this plan):**

  - **C1** (Not started) — SDK surface decision record: which namespaces the SDK
    path exposes, and the `Core.Domain` exclusions. Acceptance: each excluded
    namespace is listed with its baseline entries.
  - **C2** (Not started) — Host-side Npgsql adapter design (name and location).
    Acceptance: only the composition host references Npgsql.
  - **C3** (Not started) — Public API snapshot tests for `Abstractions` and
    `Modules`. Acceptance: snapshots exist and fail on any unreviewed public
    change.
  - **C4** (**Unverified.** Not compiled.) — **Kernel package-consumer test:** a
    project that references only the packages and compiles a minimal module
    (first executable deliverable). Acceptance: builds with `dotnet build`
    against packages only; architecture test confirms no `Domain`,
    `Application`, `Persistence`, `Api`, Npgsql or `engine_core` reference.
  - **C5** (Not started) — Pinned cross-repo release manifest (see §4).
    Acceptance: manifest lists every package version and contract version; no
    floating reference.
  - **C6** (Blocked) — Extraction PR. Acceptance: blocked until Tracks A and B
    pass and C1 to C5 are accepted.

## 4. Pinned cross-repo release manifest (proposed)

One manifest, owned by `cadence-clinical` (the composition authority, ADR-2601).
It is the only place a product repository's version is pinned into the host.

Each entry records:

- package or contract name and exact semver version;
- source repository and source revision (SHA);
- public API snapshot hash (from C3);
- contract compatibility test result, with the run identifier and whether it was
  local or CI;
- status: `verified`, `unverified` or `blocked`.

Rules:

- No entry may reference a branch head.
- An entry with `unverified` status may not appear in a release candidate.
- Rolling back means changing a pinned version back to the previous entry.
  Nothing else changes.

## 5. Acceptance criteria per slice

These restate the shared rubric in [MIGRATION_PLAN.md](MIGRATION_PLAN.md) as
checks a reviewer can run.

1. **Ownership:** each moved file has one owner in the matrix. No file has two.
2. **Correctness:** required tests run and pass against the recorded revision. A
   skip counts as not run.
3. **Integrity:** transaction, authorization, audit and integrity tests from §4
   of the earlier draft pass with zero skips against PostgreSQL.
4. **Boundaries:** `K-XS-1` and `K-MOD-1` baseline entries for the slice's files
   are removed, not moved. The baseline never grows.
5. **Compatibility:** pinned versions in the manifest; consumer test passes.
6. **Provenance:** source history and license obligations are recorded.
7. **Reversibility:** the rollback in §6 is rehearsed before the slice merges.

## 6. Rollback strategy

- **Source stays authoritative** in `cadence-clinical` until a slice passes §5.
  No slice deletes its source in the same change.
- **Per-slice revert:** each slice is one change with its source SHA recorded.
  Rollback reverts that change and restores the previous manifest entry.
- **Database:** no schema ownership changes implicitly. Any migration in a slice
  carries a tested down path, or a written reason it is forward-only.
- **Single writer:** while a slice is live, only one repository writes a given
  table. The owner is recorded in the matrix.
- **Retirement:** source is deleted only in a later PR, after the slice has run
  in the composed host for an agreed period.
- **Rehearsal:** rollback runs once against a PostgreSQL copy with synthetic
  data before C6 merges. **Not yet performed.**

## 7. Risks

- **Skipped evidence reads as success.** Until Track A records a zero-skip run,
  no requirement can be called qualified.
- **Moved violations keep their rule.** A type moved to a new assembly with the
  same prohibited reference is new debt under a new name. Each slice must
  resolve its violations, not relocate them.
- **Tooling coupling.** `packages/tooling_core` references every application
  project (per the review). Classify it separately from business-domain
  dependencies so extraction does not become a package-level distributed
  monolith.
- **SDK drags persistence.** EF Core stays in `Modules` by design (ADR-2602
  §3.2). Upgrading it is a coordinated, pinned change.
- **Green build is not validation.** Nothing in this plan is validated by a
  build alone.

## 8. What this plan does not do

- It does not move, delete or modify implementation code.
- It does not decide the 107 `needs decision` rows.
- It does not change the five-gates roadmap (#5310) or the current runtime.
- It does not claim any test or consumer result. Every such item is marked
  unverified until a run is recorded.

## 11. Track C status (draft, 2026-10-28)

Everything in this section is **unverified**. No .NET SDK is available in the
authoring environment (`dotnet` is missing and the installer download is
refused), so nothing below was compiled or run.

- **C1 SDK surface decision** (Draft) — Public SDK types come from
  `Cadence.Kernel` (`TenantId`), `Cadence.Kernel.Commands` (`ICommand`,
  `CommandResult`, `KernelError`, `Actor`), `Cadence.Kernel.Lineage` and
  `Cadence.Kernel.References` in `Abstractions`, plus `Cadence.Kernel.Modules`.
  The `Core.Domain` exclusions wait on ADR-2602 §8 item 2.
- **C2 Host-side Npgsql adapter** (Draft) —
  `Cadence.Kernel.Modules/ModuleCommandServiceCollectionExtensions.cs` imports
  `Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure` directly. The adapter
  must take that code out of the SDK.
- **C3 Public API snapshots** (Not started) — Snapshot tool not chosen. It needs
  the C1 surface first.
- **C4 Package-consumer test** (Draft, **unverified**) —
  `packages/kernel/consumer-tests/Cadence.Kernel.ConsumerTests`. Two contract
  tests reference only `Abstractions` and `Modules`. It is **not in
  `Cadence.sln`**, and the test-layout rules are not changed. Adding it to the
  solution is a separate step.
- **C5 Release manifest** (Draft, below) — Every entry is `unverified`, so none
  may appear in a release candidate.

### C4 finding that limits the consumer test

`ICommandContext` and `IKernelUnitOfWork` in
`Cadence.Kernel.Modules/Commands/CommandHandlerContracts.cs` put
`Cadence.Core.Domain.AggregateRootBase` in their **public signatures**
(`FindAsync<TAggregate>` and related). Any module that uses the unit of work
therefore needs `Core.Domain` at compile time. The C4 test avoids the unit of
work for that reason. A consumer that handles aggregates cannot pass the
boundary check until ADR-2602 §8 item 1 moves or redefines `AggregateRootBase`
and `IGxPAuditable`. That step, not the test, is the real gate for C4.

### C5 pinned release manifest (proposed entries)

| Package or contract                 | Source repository                      | Pinned revision         | Version       | Status     |
| ----------------------------------- | -------------------------------------- | ----------------------- | ------------- | ---------- |
| `Cadence.Kernel.Abstractions`       | `cadence-clinical` (`packages/kernel`) | `b43e88c`               | not published | unverified |
| `Cadence.Kernel.Modules`            | `cadence-clinical` (`packages/kernel`) | `b43e88c`               | not published | unverified |
| Public API snapshot, `Abstractions` | `cadence-clinical`                     | not created (C3)        | none          | blocked    |
| Public API snapshot, `Modules`      | `cadence-clinical`                     | not created (C3)        | none          | blocked    |
| C4 consumer test run                | `cadence-clinical`                     | `b43e88c` + this change | none          | unverified |

Rules that apply to the manifest, per §4 above: entries use exact versions and
SHAs, never branch heads. `unverified` and `blocked` entries stay out of any
release candidate.
