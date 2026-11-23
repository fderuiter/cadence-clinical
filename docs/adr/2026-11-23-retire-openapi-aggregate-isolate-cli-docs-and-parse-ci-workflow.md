# ADR-2539: Retire OpenAPI Aggregate, Isolate Generated CLI Reference Pages, and Parse ci.yml at Runtime

- **Status:** Accepted (amends [ADR-2534](2026-10-20-quality-gates-live-in-one-registry-and-each-must-be-able-to-fail.md))
- **Date:** 2026-11-23
- **Authors:** @fderuiter, Stitch Agent
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-053 (development guardrails: deterministic, conflict-free contract and CI verification)
- **Related:** [ADR-2534](2026-10-20-quality-gates-live-in-one-registry-and-each-must-be-able-to-fail.md), [ADR-2536](2026-11-22-unify-18-module-migrations-and-strip-schema-doc-counts.md), [ADR-2816](2026-11-19-retire-orphaned-legacy-web-type-schemas.md)

---

## 1. Context & Problem Statement

Parallel pull requests touching independent microservices, CLI commands, quality gates, or GitHub Actions workflow steps collided on four shared generated or hand-mirrored artifacts:

1. `packages/tooling_core/src/Cadence.OpenApiExporter/Program.cs` merged all 17 `.NET` service contracts into `docs/openapi/cdisc_openapi.json` (41,538 lines). Per [ADR-2816](2026-11-19-retire-orphaned-legacy-web-type-schemas.md), web type generation (`apps/web/scripts/generate-api-types.js` and `ApiClientValidatorService.cs`) consumes per-service `docs/openapi/<service>_openapi.json` files while legacy Python-era tooling reads `docs/openapi/legacy/cdisc_openapi.json`, and `apps/gateway/src/Program.cs` constructs YARP routes in memory from `ServiceTopology.Services`. The top-level `docs/openapi/cdisc_openapi.json` had zero runtime or build-time consumers yet conflicted whenever two branches altered endpoints in different services.
2. `packages/tooling_core/src/Services/ApiDocsGeneratorService.cs` wrote volatile per-service operation counts into `docs/api/index.md`, causing endpoint changes in any microservice to modify the shared API index alongside `docs/api/<service>.md`.
3. Under [ADR-2534](2026-10-20-quality-gates-live-in-one-registry-and-each-must-be-able-to-fail.md), `packages/cli/src/Services/GateDocs.cs` embedded column-padded Markdown tables and total gate counts inside `<!-- quality-gates:begin -->` regions in `docs/cli.md` and `docs/CI.md`. Adding one gate with a longer description widened every row of both narrative documents, and Prettier reformatted inline Markdown tables unless excluded. Similarly, CLI subcommand additions in `packages/cli/src/Program.cs` required manual prose edits in `docs/cli.md` to satisfy `CliDocsGeneratorService`.
4. `packages/cli/src/Services/CiPlan.cs` manually duplicated 11 jobs, `Needs` lists, `Always` flags, and step command arrays from `.github/workflows/ci.yml` while `tests/Cadence.UnitTests/Packages/LocalCiPlanTests.cs` already parsed `.github/workflows/ci.yml` with an indentation-aware, reflection-free parser.

## 2. Decision Drivers & Constraints

- **Eliminate cross-branch merge conflicts:** independent endpoint PRs must touch only `docs/openapi/<service>_openapi.json` and `docs/api/<service>.md`; adding a CLI command or quality gate must produce a single-row unpadded diff in standalone generated pages without rewriting `docs/cli.md` or `docs/CI.md`.
- **Preserve ADR-2534 single-registry enforcement:** `packages/cli/src/Services/QualityGates.cs` remains the single source of truth for quality gates, `QualityGateRegistryTests` continues proving every gate fails on a breaking fixture, and `cadence check cli-docs` continues failing closed on stale documentation.
- **Maintain strict Native AOT compatibility:** `packages/cli/src/Cadence.Cli.csproj` compiles with `<PublishAot>true</PublishAot>` and `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, requiring source-generated `[GeneratedRegex]` parsing with zero reflection-based YAML dependencies.
- **Preserve legacy boundaries and GxP snapshots:** `docs/openapi/legacy/cdisc_openapi.json`, `apps/web/src/api/types.ts`, YARP gateway routing, VitePress link/sidebar synthesis, and committed GxP snapshots under `docs/SDLC/` must remain untouched.

## 3. Options Considered

### Option 1: Retire `docs/openapi/cdisc_openapi.json`, move API counts per service, isolate unpadded CLI reference pages, and parse `ci.yml` at runtime (Selected)

- **Overview:** Remove `AggregateOpenApiSpecs` and delete `docs/openapi/cdisc_openapi.json` while letting `OpenApiSyncService.StaleSpecs` reject any reintroduced top-level `cdisc_openapi.json` (ignoring `docs/openapi/legacy/`); move operation counts from `docs/api/index.md` into `docs/api/<service>.md`; amend ADR-2534 so `GateDocs` writes unpadded, count-free tables to standalone `docs/cli-gates.md` and `CliDocsGeneratorService` emits an unpadded command inventory to `docs/cli-commands.md` (both excluded in `.prettierignore` and registered as `Generated` in `DocIndexSyncService`); and extract `WorkflowYamlParser` into `packages/cli/src/Services/` so `CiPlan` derives job order, `Needs`, `Always`, and standard step commands directly from `.github/workflows/ci.yml` with a compact local-override map.
- **Pros:**
  - ✅ Endpoint PRs in separate services never touch shared OpenAPI or `docs/api/index.md` files.
  - ✅ Gate and CLI command additions produce 1-line diffs in standalone generated files without Prettier table-padding churn or `docs/CI.md` conflicts.
  - ✅ Standard step and dependency edits in `.github/workflows/ci.yml` propagate automatically to `pnpm cadence ci` while `LocalCiPlanTests` retains fail-closed parity checks.
- **Cons:**
  - ❌ `WorkflowYamlParser` assumes the repository's two-space YAML formatting enforced by Prettier and `actionlint`.

### Option 2: Keep aggregate files and inline marker regions with custom Git merge drivers

- **Overview:** Retain `docs/openapi/cdisc_openapi.json`, inline `quality-gates` regions, and hand-coded `CiPlan.cs` steps while documenting local Git merge drivers.
- **Pros:**
  - ✅ Smaller initial code change.
- **Cons:**
  - ❌ GitHub server-side mergeability checks ignore local `.git/config` merge drivers, leaving parallel PRs conflicted on GitHub and keeping manual `CiPlan.cs` duplication intact.

## 4. Decision Outcome

- **Chosen Option:** Option 1.
- `Cadence.OpenApiExporter` exports only per-service `docs/openapi/<service>_openapi.json` contracts; `docs/openapi/cdisc_openapi.json` and `AggregateOpenApiSpecs` are deleted, and `OpenApiSyncService` rejects any reintroduced top-level `cdisc_openapi.json` via `StaleSpecs` while ignoring `docs/openapi/legacy/`.
- `ApiDocsGeneratorService` renders `The service exposes <count> operation(s).` (or `The service exposes no operations.`) inside each `docs/api/<service>.md` header and renders only `[service](service.md)` and `` `docs/openapi/<service>_openapi.json` `` in `docs/api/index.md`.
- Amending [ADR-2534](2026-10-20-quality-gates-live-in-one-registry-and-each-must-be-able-to-fail.md), `GateDocs` writes standalone unpadded `docs/cli-gates.md` without gate counts and fails `GateDocs.Check` if `<!-- quality-gates:begin` reappears in `docs/cli.md` or `docs/CI.md`. `CliDocsGeneratorService` emits and `--check`-validates standalone unpadded `docs/cli-commands.md` from `packages/cli/src/Program.cs`. Both files are listed in `.prettierignore` and indexed as `Generated` in `docs/DOCUMENTATION_INDEX.md`.
- `WorkflowYamlParser` in `packages/cli/src/Services/WorkflowYamlParser.cs` parses `.github/workflows/ci.yml` using `[GeneratedRegex]`, and `CiPlan` builds `CiJob` and `CiStep` lists at runtime while keeping a compact override map only for `GitHubOnlyJobs`, `CoveredBy`, `EnvironmentIfPassed`, `RequiredTool`, `NeedsPostgres`, `InProcess` compliance/database handlers, `RestoreFiles`, `Files`, `NotRunLocally`, container matrix targets, and `AotRuntimeIdentifier` publishing.

## 5. Consequences & Trade-offs

- **Positive Impact:** unrelated microservice, CLI, quality-gate, and CI workflow branches touch disjoint lines or files and merge cleanly under standard three-way Git merge.
- **Negative Impact / Technical Debt:** `WorkflowYamlParser` intentionally supports the two-space GitHub Actions YAML subset used by `.github/workflows/*.yml` rather than arbitrary YAML anchors or tags.
- **Mitigation Strategy:** `.github/workflows/*.yml` formatting is enforced by Prettier and `actionlint`, and `LocalCiPlanTests` fails closed whenever an unmapped `uses:` action or unrecognized workflow command appears.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/tooling_core` (`Cadence.OpenApiExporter`, `ApiDocsGeneratorService`, `CliDocsGeneratorService`, `DocIndexSyncService`, `SelfHealService`), `packages/cli` (`GateDocs`, `WorkflowYamlParser`, `CiPlan`, `ToolCommands`, `QualityGates`, `Program`), `.prettierignore`, `docs/api/`, `docs/cli-gates.md`, `docs/cli-commands.md`, `docs/cli.md`, `docs/CI.md`, `docs/DOCUMENTATION_INDEX.md`.
- **Verification Plan:** `OpenApiDriftTests`, `QualityGateRegistryTests`, `CliDocsParityTests`, `DocIndexSyncServiceTests`, `LocalCiPlanTests`, `docs/.vitepress/navigation.test.mjs`, `dotnet publish packages/cli/src/Cadence.Cli.csproj -c Release`, `pnpm cadence check openapi`, `pnpm cadence check cli-docs`, `pnpm cadence check prettier`, `pnpm cadence check api-clients`, `pnpm cadence check doc-index`, `pnpm cadence check markdown`, `pnpm cadence check adrs`, and `CADENCE_DOCS_SKIP_CHECKS=1 pnpm docs:build`.
