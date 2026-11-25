# ADR-2834: Shard Sentinel and Compliance Baselines Per Leaf Owner

- **Status:** Accepted
- **Date:** 2026-11-24
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001, PRD-SEC-001

---

## 1. Context & Problem Statement

After `kernel-boundaries/` and `kernel-rule-coverage/` were partitioned into per-assembly and per-rule-area shards ([ADR-2823](2026-11-23-shard-ratchet-baselines-and-scope-deid-pruning.md)), five remaining shrink-only baseline gates still shared monolithic files or coarse top-level directory shards across concurrent remediation pull requests (PRD-SYS-001, PRD-SEC-001):

1. `permission-matrix.baseline.txt` (formerly under `tests/Cadence.ArchitectureTests/ArchitectureBaselines/`) stored all 1,400 static RBAC `<role>|<resource>|<action>` grants across 83 resources in a single ordinally sorted file, causing adjacent hunk conflicts whenever independent PRs tightened permissions on adjacent resources.
2. `direct-state-writes.baseline.txt` (formerly under `tests/Cadence.ArchitectureTests/ArchitectureBaselines/`) stored all 134 legacy `SaveChanges` / `SaveChangesAsync` IL call sites across 14 production assemblies in a single file, causing hunk collisions across adjacent persistence assemblies.
3. `cross-service-isolation.baseline.txt` (formerly under `tests/Cadence.ArchitectureTests/ArchitectureBaselines/`) combined `IL|...` and `SOURCE|...` sibling-service coupling entries across distinct `(sourceService, targetService)` pairs in one file.
4. `baselines/deid/` (`256` entries across `34` files) and `baselines/secrets/` (`41` entries across `19` files) grouped entries by coarse `apps/<name>`, `packages/<name>`, and `root` units rather than per tracked source file, producing hunk collisions whenever two PRs cleaned up different files inside `apps/web/` (`19` DEID files) or `apps/execution/` (`7` secret files).
5. `CADENCE_UPDATE_BASELINE=1` was not consistently wired as a shrink-only pruner across `PermissionMatrixRatchetService` (`check permission-matrix`) and `ImportValidatorService` (`check imports`), and `BaselineRatchetService` only aggregated four sharded gate prefixes rather than all seven sharded baseline gates.

## 2. Decision Drivers & Constraints

- **Conflict-Free Parallel Debt Burn-Down (PRD-SYS-001, PRD-SEC-001):** Independent pull requests paying down baseline debt in disjoint RBAC resources, persistence assemblies, `(sourceService, targetService)` pairs, or tracked source files must merge cleanly under Git's standard 3-way merge (`git merge-file` / `git merge-tree`).
- **Zero Custom Merge Drivers ([ADR-2287](2026-10-08-remove-gitattributes-union-merge-drivers.md), [ADR-2532](2026-10-20-decouple-gxp-snapshots-from-feature-prs.md)):** Never add `.gitattributes` `merge=union` or custom merge drivers, which Github's web/merge-queue engine ignores and which bypass semantic review.
- **Native-AOT CLI Integrity ([ADR-2534](2026-10-20-quality-gates-live-in-one-registry-and-each-must-be-able-to-fail.md), [ADR-2832](2026-11-23-expand-il-sentinels-and-shrink-only-cli-ratchets.md)):** Keep `Cadence.Cli` (`PublishAot=true`) free of `Mono.Cecil` and reflection warnings while preserving full IL parity in `Cadence.ArchitectureTests`.
- **Strict Shrink-Only Ratchets & Gate Multiset Continuity:** `CADENCE_UPDATE_BASELINE=1` must only subtract resolved entries and delete empty shards (plus empty parent directories), never adding new violations or creating new shards, while `BaselineRatchetService` aggregates all seven sharded gates (and maps retired monolithic paths to their sharded gate keys) so shard splits produce zero net multiset growth.

## 3. Options Considered

### Option 1: Partition All Remaining Sentinel and Compliance Baselines Per Leaf Owner Under `baselines/` (Selected)

- **Overview:** Partition `permission-matrix` into `baselines/permission-matrix/<resource>.baseline.txt` (`83` shards), `direct-state-writes` into `baselines/direct-state-writes/<Assembly>.baseline.txt` (`14` shards), `cross-service-isolation` into `baselines/cross-service-isolation/<sourceService>/<targetService>.baseline.txt` (`2` shards), `deid` into `baselines/deid/files/<sourceFile>.baseline.txt` (`34` shards), and `secrets` into `baselines/secrets/files/<sourceFile>.secrets.baseline.json` (`19` single-file JSON shards alongside `baselines/secrets/config.json`); wire `CADENCE_UPDATE_BASELINE=1` shrink-only pruning + empty-shard/directory deletion across `ShrinkOnlyBaseline`, `PermissionMatrixRatchetService`, `ImportValidatorService`, and `DeidComplianceService`; register all seven sharded gates in `BaselineRatchetService`; and fail closed if any legacy monolithic or unit-level baseline file reappears.
- **Pros:**
  - Eliminates adjacent-line Git 3-way merge collisions across all `83` RBAC resources, `14` persistence assemblies, sibling service pairs, `34` DEID-baselined files, and `19` secret-baselined files without custom Git merge drivers.
  - Preserves matched-token digest hashing in `DeidComplianceService`, temporary merged-JSON batching in `DetectSecretsGateService`, and `PublishAot=true` compilation of `Cadence.Cli`.
  - Automatically deletes empty shard files and empty parent directories as legacy debt reaches zero.
- **Cons:**
  - Increases small tracked shard file count under `baselines/` (`152` leaf shards across the five migrated gates), which shrinks monotonically as debt is paid down.

### Option 2: Keep Monolithic Sentinel Baselines and Coarse App/Package Compliance Shards

- **Overview:** Retain `permission-matrix.baseline.txt`, `direct-state-writes.baseline.txt`, `cross-service-isolation.baseline.txt`, `baselines/deid/<unit>.baseline.txt`, and `baselines/secrets/<unit>.secrets.baseline.json`, relying on branch rebases when adjacent lines collide.
- **Pros:**
  - Fewer baseline files on disk initially.
- **Cons:**
  - Continues to trigger deterministic Git 3-way hunk conflicts whenever concurrent PRs remediate adjacent RBAC resources, persistence assemblies, or different files within `apps/web/` or `apps/execution/`.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Sharding every remaining ratchet baseline by its deterministic leaf owner eliminates cross-owner and intra-unit Git merge conflicts under standard 3-way merge while preserving fail-closed multiset ratcheting, Native-AOT CLI execution, and shrink-only pruning.

## 5. Consequences & Trade-offs

- **Positive Impact:** Concurrent debt paydowns across distinct RBAC resources, persistence assemblies, service pairs, and tracked source files touch disjoint files (or clean file deletions) and merge with zero conflicts in `git merge-file`, `git merge-tree`, and GitHub's merge queue.
- **Negative Impact / Technical Debt:** Legacy sentinel and compliance exemptions now reside across `baselines/{permission-matrix,direct-state-writes,cross-service-isolation,deid/files,secrets/files}/` until remediated.
- **Mitigation Strategy:** All seven sharded baseline gates reject empty, unsorted, misplaced, or legacy files, enforce multiset non-growth in `BaselineRatchetService`, and automatically delete empty shards and parent directories when `CADENCE_UPDATE_BASELINE=1` prunes resolved entries.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `baselines/`, `packages/tooling_core`, `packages/cli`, `tests/Cadence.ArchitectureTests`, `tests/Cadence.UnitTests`, `AGENTS.md`, `CONTRIBUTING.md`, `docs/CI.md`, `docs/TESTING.md`, `docs/cli.md`, `docs/ENVIRONMENT_VARIABLES.md`.
- **Verification Plan:** Verified via `dotnet build Cadence.sln`, `dotnet publish packages/cli/src/Cadence.Cli.csproj -c Release -r linux-x64`, `dotnet test tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj`, `dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj`, pairwise `git merge-file` simulations across all sharded gates, and `pnpm cadence check`.
