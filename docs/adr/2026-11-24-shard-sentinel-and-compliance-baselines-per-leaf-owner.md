# ADR-2686: Shard Sentinel and Compliance Baselines Per Leaf Owner

- **Status:** Accepted
- **Date:** 2026-11-24
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001, PRD-SEC-001
- **Related Issues / PRs:** Supersedes #6253; amends [ADR-2832](2026-11-23-expand-il-sentinels-and-shrink-only-cli-ratchets.md)

---

## 1. Context & Problem Statement

Concurrent feature branches paying down legacy architectural or compliance debt repeatedly collided in two classes of baseline artifacts (PRD-SYS-001, PRD-SEC-001):

1. **Monolithic architecture sentinel baselines (`ADR-2832`):** Legacy sentinel files under `tests/Cadence.ArchitectureTests/ArchitectureBaselines/` (`permission-matrix.baseline.txt` with 1,400 grants across 83 RBAC resources, `direct-state-writes.baseline.txt` with 134 `SaveChanges*` call sites across 14 assemblies, and `cross-service-isolation.baseline.txt` with 12 sibling-service references across 2 service pairs) stored unrelated domains in single sorted text files. Parallel pull requests touching adjacent lines or pruning debt in different resources or assemblies produced avoidable merge conflicts.
2. **Coarse unit-level compliance shards:** `baselines/deid/<unit>.baseline.txt` (for example `baselines/deid/apps/web.baseline.txt`, holding over 200 of the 252 baselined DEID entries across 32 tracked files) and `baselines/secrets/<unit>.secrets.baseline.json` (32 findings across 13 tracked files) grouped every file inside an entire app or package into one shared shard. Any two pull requests touching different Vue components or documents inside `apps/web`, `packages/ui`, or `root` risked `modify/modify` or `modify/delete` conflicts on the shared unit file.

In addition, `CADENCE_UPDATE_BASELINE=1` was only partially wired across CLI and xUnit sentinels, and gate path constants and shard validation logic were duplicated across CLI and `ToolingCore` services.

## 2. Decision Drivers & Constraints

- Eliminate cross-PR merge conflicts when independent pull requests pay down baseline debt in unrelated RBAC resources, assemblies, service pairs, or source files.
- Keep `Cadence.Cli` 100% Native-AOT compatible (`PublishAot=true`, zero `IL2xxx`/`IL3xxx` warnings) and fast in `--pre-commit`, full-repo `pnpm cadence check`, and CI ([ADR-2534](2026-10-20-quality-gates-live-in-one-registry-and-each-must-be-able-to-fail.md)).
- Enforce strict shrink-only semantics (`CADENCE_UPDATE_BASELINE=1` subtracts resolved entries and deletes empty shards plus empty parent directories, never adding new violations) and fail closed on empty, unsorted, duplicate, misplaced, or legacy baseline files.
- Centralize sharded gate metadata and text-shard validation/pruning in `Cadence.ToolingCore` so `Cadence.Cli` and `Cadence.ArchitectureTests` share one contract.

## 3. Options Considered

### Option 1: Per-Leaf-Owner Baseline Shards with Shared `BaselineGateCatalog` and `TextBaselineShardStore` (Selected)

- **Overview:** Split all five baseline families to their finest natural leaf owner under `baselines/`, centralize gate directories and legacy-path mappings in `BaselineGateCatalog`, centralize `*.baseline.txt` parsing/validation/pruning in `TextBaselineShardStore`, and wire `CADENCE_UPDATE_BASELINE=1` shrink-only pruning across CLI and xUnit sentinels:
  - `baselines/permission-matrix/<resource>.baseline.txt` (83 resource shards, 1,400 entries)
  - `baselines/direct-state-writes/<Assembly>.baseline.txt` (14 assembly shards, 134 entries)
  - `baselines/cross-service-isolation/<sourceService>/<targetService>.baseline.txt` (2 service-pair shards, 12 entries)
  - `baselines/deid/files/<sourceFile>.baseline.txt` (32 per-file shards, 252 entries)
  - `baselines/secrets/files/<sourceFile>.secrets.baseline.json` plus `baselines/secrets/config.json` (13 per-file shards, 32 entries)
- **Pros:**
  - Paying down debt in one component, resource, assembly, or service pair modifies or deletes only its dedicated leaf file, turning concurrent debt paydowns into clean non-overlapping Git operations.
  - `BaselineRatchetService` aggregates entries across all 7 sharded gates (`kernel-boundaries`, `kernel-rule-coverage`, `permission-matrix`, `direct-state-writes`, `cross-service-isolation`, `baselines/deid`, and `baselines/secrets`) with legacy-path migration compatibility, preserving file-and-count-scoped `Baseline-Growth` trailer enforcement.
  - `BaselineGateCatalog` and `TextBaselineShardStore` remove duplicated path constants and shard validation code across `PermissionMatrixRatchetService`, `ImportValidatorService`, `DeidComplianceService`, `DetectSecretsGateService`, `SecretsBaselineCleanerService`, `BaselineRatchetService`, and `ShrinkOnlyBaseline`.
- **Cons:**
  - Increases the number of small files under `baselines/` (144 leaf shards across the five migrated gates), mitigated by automatic deletion of empty shards and empty parent directories.

### Option 2: Retain Monolithic Sentinel Files and App/Package Compliance Shards with Custom Merge Drivers

- **Overview:** Keep the three monolithic sentinel baseline files and unit-level `apps/<name>` / `packages/<name>` shards, relying on custom Git merge drivers or manual conflict resolution.
- **Pros:**
  - Fewer files under `baselines/`.
- **Cons:**
  - GitHub's server-side merge queue and `git merge-tree` conflict checks do not execute local `.gitattributes` merge drivers ([ADR-2532](2026-10-16-feature-branches-never-commit-the-gxp-snapshot.md)), leaving `apps/web` and monolithic sentinel baselines prone to merge queue rejections.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Leaf-owner sharding aligns Git's file-level merge model directly with architectural and source-file ownership, works natively with GitHub merge queues and `git merge-tree`, and shares one AOT-safe validation and shrink-only pruning implementation across CLI and xUnit gates.

## 5. Consequences & Trade-offs

- **Positive Impact:** Independent branches pruning RBAC grants, `SaveChanges*` call sites, sibling-service imports, DEID allowlist digests, or `detect-secrets` allowlist entries no longer contend on shared baseline files. Legacy monolithic and unit-level baseline paths fail closed if reintroduced.
- **Negative Impact / Technical Debt:** Active branches created prior to the migration that touch retired baseline paths must re-target the per-owner shard layout.
- **Mitigation Strategy:** `BaselineRatchetService` maps retired monolithic and unit-level paths onto the canonical gate keys across historical commit ranges, `cadence clean-secrets-baseline` normalizes secret shards deterministically, and `CADENCE_UPDATE_BASELINE=1` prunes stale entries and deletes empty shards automatically.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/tooling_core`, `packages/cli`, `tests/Cadence.ArchitectureTests`, `tests/Cadence.UnitTests`, `baselines/`, `AGENTS.md`, `CONTRIBUTING.md`, `docs/TESTING.md`, `docs/ENVIRONMENT_VARIABLES.md`, `docs/CI.md`, `docs/cli.md`, `docs/adr/index.md`, `docs/DOCUMENTATION_INDEX.md`.
- **Verification Plan:** Verified via `dotnet build Cadence.sln`, Native-AOT `dotnet publish packages/cli/src/Cadence.Cli.csproj -c Release -r linux-x64`, `dotnet test tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj`, `dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj`, and `pnpm cadence check`.
