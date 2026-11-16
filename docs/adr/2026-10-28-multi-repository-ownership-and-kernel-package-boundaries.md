# ADR-2602: Multi-Repository Ownership and Kernel Package Boundaries

- **Status:** Accepted (approved by @fderuiter on 2026-10-28 as the recommendations in this draft)
- **Date:** 2026-10-28
- **Authors:** @fderuiter, Claude Code
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-053
- **Issues:** #4719 (kernel epic), #5310 (five-gates epic), #4787 (scaffold dispositions)
- **Reconciles:** [ADR-2600](2026-10-19-modules-write-through-the-kernel-command-path.md) and [ADR-2610](2026-10-15-scaffold-services-become-modules-merge-or-are-deleted.md). Both remain **Accepted**. This ADR does not supersede either of them.
- **Builds on:** [ADR-2601](2026-10-19-one-composition-host-for-the-kernel-and-its-modules.md) (one composition host)
- **Records:** [Repository registry](../architecture/repository-migration/REPOSITORY_REGISTRY.md), [capability ownership matrix](../architecture/repository-migration/CAPABILITY_OWNERSHIP_MATRIX.md), [migration plan](../architecture/repository-migration/MIGRATION_PLAN.md), [multi-repository agent execution guide](../architecture/repository-migration/AGENT_HANDOFF.md)
- **Shared inventory:** [`SOURCE_INVENTORY.md`](../architecture/repository-migration/SOURCE_INVENTORY.md) (task 1), merged to `main` in #5681 and carrying the symbol-level review merged in #5683 (`eab3c1e`).

> [!NOTE]
> This ADR is **accepted** as of 2026-10-28. Acceptance settles the direction only. It does not start extraction, which
> stays gated on the migration plan's remaining conditions. Previously, it recorded options and a recommended direction for review. It did not finalize the
> ownership matrix, the package graph or the repository layout. Items marked *needs inventory* wait for the shared
> file-level inventory (task 1) before anyone decides them. No code, package or repository has moved.

---

## 1. Context & Problem Statement

The migration program splits the program across ten repositories. Repository ownership is not the same as runtime
structure: a repository governs source, and a product is a purchasable application. Two accepted decisions constrain
the runtime:

- **ADR-2610** makes every scaffold service a `Cadence.Modules.*` module hosted in one composition host.
- **ADR-2600** requires every clinical write, kernel or module, to commit through one PostgreSQL transaction. A module
  context enlists in the kernel's transaction on a shared database.

Splitting source across repositories raises questions neither ADR answers:

1. Does a product repository become a runtime service, a separate database, or a package that the host composes?
2. The public SDK, `Cadence.Kernel.Modules`, references EF Core, Npgsql and `engine_core/Cadence.Core.Domain`. A product
   that consumes it inherits all three. The registry says products must not depend on kernel persistence.
3. `MIGRATION_PLAN.md` requires this ADR to reconcile ADR-2610 and ADR-2600 explicitly and forbids silently reclassifying
   them as superseded.

Current kernel project graph on `main` (`e3955e1`): `Cadence.Kernel.Modules` references `Abstractions` and
`engine_core/Cadence.Core.Domain`. It also carries EF Core Relational, Npgsql, FluentValidation and
`Microsoft.AspNetCore.OpenApi`. Module authors need EF Core because a module defines a `ModuleDbContext`. Npgsql appears
in the SDK only where the module store and the migrations helper call `UseNpgsql`. The SDK also takes `AggregateRootBase`
and `IGxPAuditable` from `Cadence.Core.Domain`. `CommandResult<TResult>` already lives in `Abstractions/Commands`.

The reference chain is transitive. `Cadence.Core.Domain` references `Cadence.Security`, which references
`Cadence.Kernel.Abstractions`. A product that takes the SDK therefore also receives `Core.Domain` and `Security`. The
inventory (`SOURCE_INVENTORY.md`, merged in #5681) records the project graph. A consumer of the SDK today also inherits these
NuGet packages: FluentValidation, `Microsoft.AspNetCore.OpenApi`, EF Core, Npgsql, `System.IdentityModel.Tokens.Jwt` and
`Microsoft.IdentityModel.Tokens`. `Abstractions` has no package references. `Core.Domain` is itself in the K-XS-1
baseline (16 entries). They sit in the Randomization, Supply and Rules namespaces, and no SDK, Abstractions, Application
or Domain file references them. Excluding those namespaces from the SDK path would clear the entries, without moving
`Core.Domain`. That is an SDK-surface decision and must be made before the SDK is published.

## 2. Decision Drivers & Constraints

- **K-XS-2:** applications reference `Cadence.Kernel.Abstractions` and `Cadence.Kernel.Modules` only.
- **K-TX-1..5 and ADR-2600 §4.2:** one transaction per command across kernel and module rows, with no two-phase commit.
- **ADR-2601 and ADR-2610:** one host and one runtime for the modules. The existing runtime is preserved.
- **Registry principles:** no cross-repo private `DbContext`, no floating dependency on another repository's `main`,
  and a composition manifest that pins verified versions.
- **Five-gates roadmap (#5310):** earlier gate evidence must keep passing after any boundary change.
- **Pre-deployment:** no dual writer and no compatibility layer for the module write path.

## 3. Options Considered

### 3.1 Runtime shape of a product repository

| Option | Summary | Verdict |
| --- | --- | --- |
| A. Product repository is its own runtime service with its own database | Each repository deploys its own host | **Rejected.** Contradicts ADR-2610 and ADR-2601. Splits the transaction that ADR-2600 requires. |
| B. Product repository produces module packages; the composition host composes them over one shared database | Each module keeps its own schema and `DbContext`, and writes through the kernel command path | **Proposed.** Keeps ADR-2600 Option A and ADR-2601 unchanged. |
| C. Per-product databases | Each product gets its own database | **Deferred.** ADR-2600 consequence: a later boundary decision, and it needs a separate ADR. |

### 3.2 Dependency shape of the public SDK (answers kernel plan §2.1 and §2.2)

| Option | Summary | Verdict |
| --- | --- | --- |
| 1. Keep EF Core, Npgsql and `Cadence.Core.Domain` in `Cadence.Kernel.Modules` | Simplest. Every product inherits every dependency. | **Rejected.** Makes the public SDK carry persistence and a non-kernel dependency. This is the most likely source of a later breaking change. |
| 2. Split the SDK | `Abstractions` holds contracts with no EF, Npgsql or `engine_core`. `Modules` keeps the EF Core base types module authors must use. Npgsql moves to a host-side adapter. `AggregateRootBase` and `IGxPAuditable` move or are redefined in kernel-owned code. The K-XS-1 members in `Core.Domain` move first. | **Proposed, pending inventory.** |
| 3. Publish `engine_core` as a shared kernel dependency | Keeps the reference and publishes it. | **Not preferred.** `engine_core` is not kernel-owned (matrix: "shared domain utilities"). |

Recommended shape under option 2, to be confirmed by K1:

- `Cadence.Kernel.Abstractions`: typed IDs, command and event contracts, `IClock`, and `CommandResult<TResult>`, which
  already lives here. No EF Core, Npgsql or `engine_core` reference. *Needs inventory:* the dependency closure
  of that result type.
- `Cadence.Kernel.Modules`: `ICommandHandler`, `ICommandContext`, `IKernelUnitOfWork`, `AddCommandHandler`,
  `ModuleDbContext` and the migrations helper. It references EF Core Relational as a **pinned, explicit** SDK dependency,
  because module authors define EF contexts (ADR-2600 §4.2). It does not reference Npgsql.
- A host-side adapter (name to be chosen in K1, for example `Cadence.Kernel.Modules.Npgsql`) supplies the Npgsql
  options and the connection provider. Only the composition host references it.
- Kernel-internal `Domain`, `Application`, `Persistence` and `Api` stay internal. Products never reference them.

### 3.3 Standards and rules assemblies (`Cadence.Rules`, `Cadence.Standards`)

- **Proposed:** keep them as separately versioned packages in the kernel repository, with one canonical registry and one
  evaluator. Authoring UI stays in Blueprint.
- **Needs decision (matrix item 4):** whether the standards registry belongs to the kernel repository or to a standards
  repository. This waits for the file-level inventory of `packages/kernel` and `apps/knowledge`.

## 4. Decision Outcome

1. **Repositories are source ownership, not runtime.** The composition host in `cadence-clinical` remains the only
   runtime composition authority (ADR-2601). Product repositories publish packages the host composes. Repository
   boundaries never create a runtime service.
2. **One database per tenant deployment** for the composed host (ADR-2600 Option A). Each module owns its schema and
   `DbContext`, and writes only through the kernel command path. A product repository never opens another module's
   `DbContext`.
3. **Public SDK packages** follow §3.2 option 2. `Abstractions` and `Modules` are the only public packages.
   `Modules` pins EF Core and does not reference Npgsql or `engine_core`.
4. **Ownership** follows the [capability ownership matrix](../architecture/repository-migration/CAPABILITY_OWNERSHIP_MATRIX.md).
   Each change has exactly one source owner and one destination context. Ambiguous classes (execution, interop, knowledge,
   fileshare, org, `packages/kernel` standards and rules) are assigned per file or per class, never per folder.
5. **Versioning:** packages are semver-versioned and pinned in the composition manifest. No floating reference to another
   repository's `main`. Public API snapshot tests gate each `Abstractions` and `Modules` release.
6. **Gating:** extraction starts only when the four conditions in the [migration plan](../architecture/repository-migration/MIGRATION_PLAN.md)
   and the [agent execution guide](../architecture/repository-migration/AGENT_HANDOFF.md#synchronization-point) hold.

### 4.1 Ownership Matrix Amendments

The matrix is not edited in this change. The amendments below are approved and go into the matrix in a follow-up docs PR:

- Add a row for **Cadence.Kernel.Modules SDK contract** (kernel) and **host-side Npgsql adapter** (clinical).
- Record `AggregateRootBase` and `IGxPAuditable` as kernel-owned contracts, once the move or redefinition is decided.
- Note that `Cadence.Security` is a transitive dependency of the SDK today.
- Keep the seven "Required resolution before extraction" items in the matrix open until the inventory closes them.

## 5. Consequences & Trade-offs

- ✅ Products depend on a small, versioned public SDK. Persistence stays internal to the kernel and the host.
- ✅ The single transaction and the single audit chain per tenant (ADR-2600) are preserved.
- ❌ EF Core remains a shared SDK dependency. Upgrading it becomes a coordinated, pinned change.
- ❌ One shared database means a cross-module schema change needs coordination. The ADR-2600 consequence on tenant-chain
  throughput still applies.
- ❌ Splitting the SDK adds one package to publish and test.

## 6. Implementation & Verification

- `Abstractions` and `Modules` public API snapshots pass.
- A consumer project that references only the packages compiles a minimal module (kernel plan K2).
- An architecture test fails if any product assembly references `Domain`, `Application`, `Persistence`, `Api`, Npgsql or
  `engine_core`.
- The boundary baseline (`kernel-boundaries.baseline.txt`, 496 lines) may only shrink.
- PostgreSQL-backed kernel tests run with zero skips before K1 closes (kernel plan §1, condition 3).

No check in this list has been run for this ADR. A green build is not validation.

### 6.1 Open Items (after acceptance)

These are implementation details inside the accepted direction. None of them reopens §4.

| # | Item | Owner of the answer |
| --- | --- | --- |
| 1 | Move or redefine `AggregateRootBase` and `IGxPAuditable` out of `Core.Domain` before the SDK is published (K1) | Kernel prep thread (task 3) |
| 2 | Exclude the 16 `Core.Domain` baseline namespaces from the SDK path, so the baseline entries clear without moving `Core.Domain` (K1) | Kernel prep thread, with the inventory's symbol review |
| 3 | Name and location of the host-side Npgsql adapter (K1) | Kernel prep thread, under §4 |
| 4 | Home of `Cadence.Rules` and `Cadence.Standards`. Still needs inventory review; not covered by this acceptance | Inventory thread, then decision |
| 5 | **Six owner choices decided in §8.1. The other 101 suggestions stand as given.** Per-class dispositions for the 107 `needs decision` inventory rows, from the symbol-level review merged in #5683. The review is read-only and not line-by-line verified, and its owners are suggestions. Each slice must confirm its file list before moving code | Inventory thread, then the slice owner (K3 and later) |
| 6 | Whether a later per-product database (option C) needs its own ADR | Deciders, after K3 |

#### 6.1.1 Owner decisions (accepted 2026-10-28)

@fderuiter approved the recommendations for the six owner choices in the inventory's symbol-level review (#5683). The
other suggestions in that review stand as given. Each decision takes effect in the matrix at the slice that moves the
code, not now.

| # | Area | Decision | Owner |
| --- | --- | --- | --- |
| 1 | Execution DbContext | Split by owner schema, one context per module, under ADR-2600. The migration plan follows the split. | platform-shell (composition) |
| 2 | Subject and consent | Participants own the workflow. Kernel keeps participant identity and `ConsentStatus`. Retire the duplicate `/consents` route and the second `SubjectConsent` in econsent. | participants |
| 3 | CGT custody | Interventions own the workflow. Custody events become kernel provenance. | interventions |
| 4 | CEC and signatures | Safety owns the CEC. Signatures bind to kernel Attestation. Confirm the consumer of `Grade3ToxicityDetected` before extraction. | safety |
| 5 | Lab reference ranges and bulk import | Evidence owns the ranges. Capture only reads them. | evidence |
| 6 | Identity and crypto helpers | Platform-shell for now. Move to kernel IdentityVault later under K006. Retire the dormant `DemographicsNormalizationService`. | platform-shell now, kernel IdentityVault later |

These decisions do not start extraction. The synchronization conditions in the migration plan still apply.

### 6.2 Rollback

This ADR changes documentation only. Reverting its pull request restores the previous state. No runtime, database or
package is affected.
