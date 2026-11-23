# ADR-2509: Retire Superseded Shared Package Engines, Shims, and Couplings

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-053

---

## 1. Context & Problem Statement

Seven shared libraries under `packages/` (`deid`, `tooling_core`, `security`, `compliance`, `engine_core`, `storage`, and `database`) retained 22 unreferenced C# files and seven dead code spans carried over from early Python-to-.NET ports and pre-consolidation scaffolding. Later architectural slices superseded all of these implementations:

- **Audit trail consolidation ([ADR-2446](2026-09-29-single-audit-writer-and-one-chain-per-aggregate.md)):** `GxPAuditInterceptor` (`packages/database`), `AuditLogEntry` / `AuditLogChainVerifier` (`packages/engine_core`), and `KernelCommandTransaction` / `KernelAuditChainVerifier` (`packages/kernel`) replaced the HMAC-keyed `AuditDigestEngine`, `IAuditLogWriter` (`InMemoryAuditStore`), `AuditSecretKey`, `AuditChainVerifier`, `AuditDigestCalculator`, and `ComplianceAuditLogRecord` ports in `packages/security` and `packages/compliance`. Despite having zero production callers, `AddCadenceSecurity` still registered `InMemoryAuditStore` and `AuditDigestEngine` singletons in every service host.
- **CLI & module consolidation ([ADR-2610](2026-10-15-scaffold-services-become-modules-merge-or-are-deleted.md)):** `Cadence.Cli` commands (`ScaffoldCommands`) and `Cadence.OpenApiExporter` / `PreCommitOpenApiService` replaced `CreateAdrService`, `ScaffoldServiceService`, `ScaffoldReadmesService`, `MergeJunitService`, `OpenApiToolingService`, `ToolingHandlers`, and `Contracts.cs` in `packages/tooling_core`, while `apps/gateway` retained an unused `<ProjectReference>` to `Cadence.ToolingCore`.
- **De-identification & outbound inference scrubbing ([ADR-2636](2026-10-20-inference-is-pluggable-residency-bound-and-sandboxed.md)):** Outbound inference scrubbing moved to `OutboundPayloadGuard` in the Intelligence module while imaging retained `DicomDeidentifier` and repository scanning retained `DeidDetector`, leaving `DeidTransforms`, `DeidAirGapVault`, `RedactionManifestBuilder`, `RedactionManifest`, and `RedactionRecordItem` with zero production callers.
- **Encrypted randomization & kernel grants ([ADR-2640](2026-10-20-randomisation-allocates-from-approved-encrypted-scheme-versions.md)):** Explicit role-aware DTO projection and encrypted scheme allocation replaced reflection-based `FieldMasker` / `BlindedFieldAttribute` in `packages/security` and the `StochasticRandomizationEngine` wrapper in `packages/engine_core`. Keeping `[BlindedField("MASKED")]` on `SubjectAllocationResult.UnblindedAuditRationale` forced `Cadence.Core.Domain` to reference `Cadence.Security` (and transitively `Microsoft.AspNetCore.App` and EF Core).
- **Commercial packaging, bitemporal queries, compliance exceptions, and storage models:** `ProductFamilyGuardMiddleware` resolves module entitlements centrally via `ResolveRequiredFamilies`, leaving `RequireProductFamilyMetadata` and `RequireProductFamily` unused; standalone bitemporal callers use `QueryAsOfAsync`, leaving `WhereAsOf` unused; `UnapprovedAIRecordException` (`packages/compliance`) and `DocumentModels.cs` (`packages/storage`) likewise had zero callers.

## 2. Decision Drivers & Constraints

- **Domain Assembly Purity (`PRD-SYS-053`):** `Cadence.Core.Domain` should remain a pure domain library without referencing `Cadence.Security` or web/persistence frameworks, and `Cadence.Apps.Gateway` should not reference build-time `Cadence.ToolingCore`.
- **Eliminate Dead DI Singletons & Shadow Implementations:** Removing `InMemoryAuditStore` and `AuditDigestEngine` from `AddCadenceSecurity` prevents accidental resolution of a non-durable, superseded audit writer alongside `GxPAuditInterceptor`.
- **Preserve Active Contracts & GxP Evidence:** All active runtime endpoints, middleware (`GatewayAuthenticationHandler`, `ProductFamilyGuardMiddleware.AllowBaselineProductFamily`, `GxPAuditInterceptor`), active helpers (`IsEncrypted`, `StandaloneInsertOnlySql`, `GetCompiledModel`, `FormBlocks`), and `[GxPRequirement]` / `[KernelRule]` tests must remain intact without shims or deprecation wrappers.
- **Repository Sentinel Compliance:** Architectural changes across `packages/security` and `apps/gateway` must keep ADR indexes, `docs/DOCUMENTATION_INDEX.md`, package `README.md` files, and path citations in `docs/ENVIRONMENT_VARIABLES.md` synchronized.

## 3. Options Considered

### Option 1: Mark Superseded Types `[Obsolete]` and Retain Project References

- **Overview:** Annotate the unused classes and methods with `[Obsolete]` while keeping the files, DI registrations, and `.csproj` references in place.
- **Pros:**
  - Avoids touching unit tests or documentation citations.
- **Cons:**
  - Fails under `TreatWarningsAsErrors` unless warnings are suppressed, leaves `InMemoryAuditStore` registered in every host's DI container, and keeps `Cadence.Core.Domain` coupled to `Cadence.Security` and `Cadence.Apps.Gateway` coupled to `Cadence.ToolingCore`.

### Option 2: Atomic Removal of Dead Engines, Shims, DI Registrations, and Project Couplings (Selected)

- **Overview:** Delete all 22 unreferenced C# files and seven dead code spans across `deid`, `tooling_core`, `security`, `compliance`, `engine_core`, `storage`, and `database`; remove `AuditLogSecretKey`, `InMemoryAuditStore`, and `AuditDigestEngine` from `AddCadenceSecurity`; drop the unused `<ProjectReference>` entries from `Cadence.Core.Domain.csproj` and `Cadence.Apps.Gateway.csproj`; delete untagged unit tests exercising only retired code; repoint active tests to `DicomDeidentifier.MinimumSigningKeyLength`, `ResponseAdaptiveEngine.Allocate`, and `PreCommitOpenApiService.FindRepoRoot`; and synchronize package READMEs and environment documentation.
- **Pros:**
  - Decouples `Cadence.Core.Domain` from `Cadence.Security` and `Cadence.Apps.Gateway` from `Cadence.ToolingCore`.
  - Removes dead DI registrations from every ASP.NET Core host and eliminates ~2,040 lines of unreferenced C# surface area in one governed change.
- **Cons:**
  - Requires coordinated updates across unit tests, package READMEs, and `docs/ENVIRONMENT_VARIABLES.md` path citations in the same pull request.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Git history and the superseding ADRs (`ADR-2446`, `ADR-2610`, `ADR-2636`, `ADR-2640`) already preserve the historical context of the retired Python-era ports. Deleting them atomically restores clean package boundaries (`Cadence.Core.Domain` and `Cadence.Apps.Gateway`) and removes unused runtime singletons without altering any active production behavior.

## 5. Consequences & Trade-offs

- **Positive Impact:** Shared packages expose only active production APIs; `Cadence.Core.Domain` compiles with `FluentValidation` as its sole package dependency; `Cadence.Apps.Gateway` no longer builds or links `Cadence.ToolingCore`; and `AddCadenceSecurity` registers only active authentication, authorization, and step-up signature components.
- **Negative Impact / Technical Debt:** `AUDIT_LOG_SECRET_KEY` and `BRAND_DOMAIN` remain in `.env.example`, `apps/apphost`, and `DevEnvironmentValidatorService` until local environment templates and remaining Python reference files in `packages/security/` are cleaned up separately.
- **Mitigation Strategy:** `docs/ENVIRONMENT_VARIABLES.md` and `docs/PYTHON_REFERENCE_INVENTORY.md` explicitly document the remaining consumers of `AUDIT_LOG_SECRET_KEY` and `BRAND_DOMAIN` so `check env-vars` continues to enforce bidirectional key and path parity.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/deid`, `packages/tooling_core`, `packages/security`, `packages/compliance`, `packages/engine_core`, `packages/storage`, `packages/database`, `apps/gateway`, `tests/Cadence.UnitTests`, and `docs/`.
- **Verification Plan:** Verified with `dotnet build Cadence.sln` (zero warnings under `TreatWarningsAsErrors`), `Cadence.ArchitectureTests`, `Cadence.UnitTests` (confirming all `[GxPRequirement]` and `[KernelRule]` tests pass, including `PRD-IMG-002` and `PRD-SYS-192`), Native AOT publish of `Cadence.Cli`, and `pnpm cadence check` (`adrs`, `doc-index`, `env-vars`, `readmes`, `markdown`, `deid`, `rtm`, and `gxp-snapshot`).
