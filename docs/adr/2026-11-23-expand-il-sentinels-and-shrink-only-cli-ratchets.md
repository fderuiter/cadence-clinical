# ADR-2832: Expand IL Sentinels and Shrink-Only CLI Ratchets

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001, PRD-SEC-001

---

## 1. Context & Problem Statement

Architectural sentinels and CLI ratchet gates previously left seven enforcement gaps across backend persistence, RBAC authorization, service boundaries, and baseline approvals (PRD-SYS-001, PRD-SEC-001):

1. Endpoints and services outside the central kernel command pipeline could call `DbContext.SaveChanges` / `SaveChangesAsync` directly (synchronously, inside async state machines, inside compiler-generated closures, or via reflection string lookups) without failing an IL sentinel.
2. Static role-to-permission grants in `PermissionMatrix.ROLE_PERMISSIONS` and inline `Grant(...)` declarations lacked a shrink-only static inventory gate, allowing silent privilege drift or shadow role dictionaries.
3. `Cadence.ArchitectureTests.csproj` did not reference every production `Cadence.*` project in `Cadence.sln` (omitting `Cadence.Modules.Execution` and `Cadence.Kernel.Host`), and GxP auditability and cascade-delete sentinels checked a static `DbContext` list rather than dynamically scanning all loaded production `DbContext` types.
4. `KernelBoundaryRules` (`K-XS-1` and `K-MOD-1`) checked raw `StudyId`, `SiteId`, and `ParticipantId` primitives but missed `SubjectId`, `TenantId`, and `[JsonPropertyName]` wire-field aliases (`studyId`, `study_id`, `siteId`, `site_id`, `participantId`, `participant_id`, `subjectId`, `subject_id`, `tenantId`, `tenant_id`).
5. `ImportValidatorService` (`imports`) only scanned `using` directives, missing qualified `Cadence.Apps.<Sibling>` namespace references and runtime reflection string lookups (`AppDomain.CurrentDomain.GetAssemblies()` + `Cadence.Apps.<Sibling>` type strings).
6. `BaselineRatchetService` accepted unscoped `Baseline-Growth: <reason>` trailers without naming the exact baseline gate path and exact `+count` delta, and did not fail closed when neither `origin/main` nor local `refs/heads/main` existed.
7. `architecture-tests` had `PreCommit = PreCommitMode.Skipped`, deferring backend IL boundary feedback until push or CI.

## 2. Decision Drivers & Constraints

- **GxP Auditability & Command Pipeline Integrity (PRD-SYS-001):** Every new state mutation must flow through the audited kernel command pipeline rather than ad-hoc `SaveChanges` / `SaveChangesAsync` calls.
- **Least-Privilege RBAC Governance (PRD-SEC-001):** Role-to-permission grants must live exclusively in `PermissionMatrix` under a shrink-only baseline verified by both xUnit architecture tests and the Native-AOT `cadence` CLI.
- **100% Production Assembly Closure:** Architecture sentinels must fail closed if any production `Cadence.*` project in `Cadence.sln` is unreferenced by `Cadence.ArchitectureTests.csproj`.
- **Native-AOT Compatibility ([ADR-2534](2026-10-20-quality-gates-live-in-one-registry-and-each-must-be-able-to-fail.md)):** `Cadence.Cli` (`PublishAot=true`) must remain free of `Mono.Cecil` and reflection warnings (`IL2xxx` / `IL3xxx`), keeping IL inspection in `Cadence.ArchitectureTests` and fast source/inventory checks in `Cadence.Cli` / `Cadence.ToolingCore`.
- **Scoped Shrink-Only Ratchets:** Baseline growth approvals must explicitly bind both the normalized gate path and exact `+count` delta (`Baseline-Growth: <path> +<count> - <reason>`).

## 3. Options Considered

### Option 1: Dual-Layer Mono.Cecil IL Sentinels + Native-AOT CLI Ratchets with File-and-Count-Scoped Approvals (Selected)

- **Overview:** Add Mono.Cecil IL sentinels in `Cadence.ArchitectureTests` for direct `SaveChanges*` calls (`DirectStateWriteRules`), cross-service IL/reflection references (`CrossServiceIsolationTests`), static RBAC reflection + source parity (`PermissionMatrixSentinelTests`), 100% `Cadence.sln` production project coverage (`ArchitectureSentinelTests`), and expanded kernel identity + `[JsonPropertyName]` wire-alias checks (`KernelBoundaryRules`); pair them with Native-AOT `permission-matrix` (`PermissionMatrixRatchetService`), expanded `imports` (`ImportValidatorService`), file-and-count-scoped `baseline-ratchet` (`BaselineRatchetService`), and `PreCommit = PreCommitMode.Quick` on `architecture-tests` (`QualityGates`).
- **Pros:**
  - Enforces 100% production project assembly coverage across `Cadence.sln`, including `Cadence.Modules.Execution` and `Cadence.Kernel.Host`.
  - Detects direct, async-state-machine, closure, and reflection-based `SaveChanges*` calls as well as cross-service reflection lookups.
  - Preserves Native-AOT compilation of `Cadence.Cli` with zero trim/AOT warnings (`45` registered gates, each verified by `QualityGateRegistryTests`).
  - Prevents blanket `Baseline-Growth` approvals from masking unrelated baseline regressions in the same commit.
- **Cons:**
  - Runs `architecture-tests` during pre-commit when backend C#/MSBuild, baseline, or test files are staged (mitigated by skipping docs-only and non-test frontend asset commits).

### Option 2: CI-Only Roslyn Analyzer / Unscoped Baseline Trailers

- **Overview:** Enforce boundaries only in CI test jobs without pre-commit execution or file-and-count-scoped baseline trailers.
- **Pros:**
  - Slightly simpler CLI surface.
- **Cons:**
  - Defers architectural feedback until CI, misses compiled IL closure/state-machine patterns when source-only, and allows a single unscoped `Baseline-Growth` trailer to mask growth across unrelated baselines.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Combining Mono.Cecil IL inspection across 100% of production assemblies in `Cadence.ArchitectureTests` with Native-AOT source/inventory ratchets and file-and-count-scoped `Baseline-Growth` trailers closes all seven enforcement gaps without sacrificing CLI startup speed or Native-AOT safety.

## 5. Consequences & Trade-offs

- **Positive Impact:** Unapproved `SaveChanges*` mutations, unbaselined RBAC grants or shadow role maps, unreferenced production projects, raw `SubjectId`/`TenantId` or wire-alias leaks, cross-service reflection coupling, and unscoped baseline growth fail immediately in pre-commit, `pnpm cadence check`, and CI.
- **Negative Impact / Technical Debt:** Existing legacy call sites are captured in shrink-only baselines (`direct-state-writes`, `permission-matrix`, `cross-service-isolation`, and `kernel-boundaries`, subsequently sharded per leaf owner under `baselines/` in [ADR-2834](2026-11-24-shard-sentinel-and-compliance-baselines-per-leaf-owner.md)).
- **Mitigation Strategy:** `ShrinkOnlyBaseline`, `PermissionMatrixRatchetService`, `ImportValidatorService`, and `KernelBoundaryBaseline` automatically prune resolved legacy entries (and delete empty shards) under `CADENCE_UPDATE_BASELINE=1` while refusing to add new entries unless an explicit file-and-count-scoped `Baseline-Growth` approval is supplied.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/cli`, `packages/tooling_core`, `tests/Cadence.ArchitectureTests`, `tests/Cadence.UnitTests`, `AGENTS.md`, `CONTRIBUTING.md`, `docs/CI.md`, `docs/TESTING.md`, `docs/cli.md`, `docs/ENVIRONMENT_VARIABLES.md`.
- **Verification Plan:** Verified via `dotnet build Cadence.sln`, `dotnet publish packages/cli/src/Cadence.Cli.csproj -c Release -r linux-x64`, `dotnet test tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj`, `dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj`, and `pnpm cadence check`.
