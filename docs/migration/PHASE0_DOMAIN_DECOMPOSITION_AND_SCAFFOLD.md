# Phase 0 Executive Discovery: Domain Decomposition Matrix & .NET 10 Solution Scaffold

**Project:** Cadence Clinical Research Software Platform
**Target Runtime:** .NET 10 (C# 14 / ASP.NET Core 10 Minimal APIs)
**Governing ADRs:** [ADR-2233](../adr/2026-09-02-migration-to-dotnet-10-architecture-and-standards.md), [ADR-2234](../adr/2026-09-02-core-service-architecture-cqrs-transactional-outbox-and-gxp-interceptor-standard.md), [ADR-2238](../adr/2026-09-03-universal-postgresql-persistence-standard-and-hybrid-jsonb-record-projection.md), [ADR-2239](../adr/2026-09-03-universal-decimal-financial-precision-dynamic-openapi-gateway-discovery-and-tier-2-quality-realignment.md)
**Relationship to prior Phase 0 documents:** This is the condensed, decision-ready output requested in the standard three-part discovery format (Executive Repository Map / Migration Sequence Matrix / CLI Scaffold). It does not replace the deep-dive audits already on file — [`SYSTEM_TOPOLOGY_AUDIT.md`](SYSTEM_TOPOLOGY_AUDIT.md), [`DEPENDENCY_AND_PARITY_CATALOG.md`](DEPENDENCY_AND_PARITY_CATALOG.md), [`BOUNDED_CONTEXT_MIGRATION_MATRIX.md`](BOUNDED_CONTEXT_MIGRATION_MATRIX.md), [`Migration_Phase0_Readiness_Audit.md`](Migration_Phase0_Readiness_Audit.md) — it sits on top of them and adds one thing they didn't have: a **verified runtime coupling graph** built by grepping the actual codebase (cross-app imports and `*_URL` service-discovery references) rather than inferring coupling from domain knowledge alone. Where that evidence refines the existing Tier assignments, it is called out explicitly below rather than silently overridden.

---

## 0. Method note: what the evidence actually shows

Two greps were run across `apps/*` (excluding `apps/web` and `apps/subject-portal`, which are JS/Vue, not Python):

1. **Cross-app Python imports** (`from apps.<other>.` / `import apps.<other>.`) in non-test source. **Result: zero.** Every cross-app Python import that exists is confined to `tests/`/`conftest.py` fixtures. Production code has no compile-time coupling between bounded contexts — the "services interact exclusively via REST" invariant in `BOUNDED_CONTEXT_MIGRATION_MATRIX.md` is not just a target constraint, it is already true today. This is good news for the migration: there is no hidden import-graph surgery required before a service can be lifted out.
2. **Runtime HTTP coupling** via `*_URL` / `*_SERVICE_URL` environment variables actually referenced in each app's source. This is the real dependency graph — who calls whom over the wire at runtime — and it is more granular than the four tiers in the existing matrix. It is used below to compute an evidence-based coupling score per context.

Because inter-service coupling is 100% REST/JSON (never a shared in-process import), **migration order is a risk/sequencing decision, not a hard compiler-enforced blocker.** A .NET 10 service can call a still-Python service's HTTP API (and vice versa) for as long as the strangler-fig cutover takes, provided the HMAC/OIDC contract on `Cadence.Contracts` is honored. The one genuine hard blocker is the shared-kernel packages (Tier 1) — every service, regardless of runtime, needs `Cadence.Core.Domain`, `Cadence.Security`, `Cadence.Contracts`, and `Cadence.Database` to exist first.

---

## 1. Executive Repository Map

Verified runtime outbound (`→`) and inbound (`←`) HTTP dependencies per context, current scaffold state, and headline external dependencies pulled directly from each service's `pyproject.toml`.

| Bounded Context | Core Python Paths | External Dependencies | Downstream Consumers (verified inbound HTTP callers) | .NET Scaffold State |
| :--- | :--- | :--- | :--- | :--- |
| **Gateway** | `apps/gateway` | fastapi, httpx, uvicorn, `packages-security` | *(edge — reverse proxy for all 17 services)* | ✅ `Cadence.Apps.Gateway.csproj` exists |
| **Execution (EDC)** | `apps/execution` | fastapi, sqlalchemy, sqlmodel, asyncpg, aioboto3, aiosmtplib, jsonschema, rapidfuzz, PyMuPDF, weasyprint | ctms, designer, etmf, quality, safety, gateway (5 domains) | ✅ `Cadence.Apps.Execution.csproj` exists |
| **Designer (MDR/USDM)** | `apps/designer` | fastapi, neo4j, weasyprint, jinja2, python-docx, docxtpl | execution, quality*, gateway | ✅ `Cadence.Apps.Designer.csproj` exists |
| **Notifications** | `apps/notifications` | fastapi, sqlalchemy, aiosmtplib, `packages-database` | econsent, etmf, execution, interop, safety, gateway (5 domains) | ⬜ not yet scaffolded |
| **eTMF** | `apps/etmf` | fastapi, sqlalchemy, PyMuPDF, cryptography, `packages-deid` | ctms, designer, econsent, eisf, execution, gateway (5 domains) | ⬜ not yet scaffolded |
| **AI Gateway** | `apps/ai_gateway` | fastapi, litellm, `packages-hexagonal`, `packages-security` | econsent, execution, interop, safety, gateway (4 domains) | ⬜ not yet scaffolded |
| **CTMS** | `apps/ctms` | fastapi, sqlalchemy, sqlmodel, asyncpg, `packages-database` | gateway only | ⬜ not yet scaffolded |
| **Knowledge** | `apps/knowledge` | fastapi, sqlalchemy, aiosqlite, asyncpg | execution, tickets, gateway | ⬜ not yet scaffolded |
| **Org / Directory** | `apps/org` | fastapi, sqlalchemy, sqlmodel, asyncpg, `packages-database` | notifications, gateway | ⬜ not yet scaffolded |
| **eISF** | `apps/eisf` | fastapi, sqlalchemy, sqlmodel, asyncpg, `packages-database` | eisf←etmf(outbound only); org, gateway (inbound) | ⬜ not yet scaffolded |
| **Econsent** | `apps/econsent` | fastapi, sqlalchemy, sqlmodel, asyncpg, `packages-database` | execution, gateway | ⬜ not yet scaffolded |
| **Interop (FHIR/Sensors)** | `apps/interop` | fastapi, sqlalchemy, httpx, `packages-deid` | org, gateway | ⬜ not yet scaffolded |
| **Quality** | `apps/quality` | fastapi, sqlalchemy, sqlmodel, asyncpg, `packages-database` | ctms, gateway | ⬜ not yet scaffolded |
| **Safety** | `apps/safety` | fastapi, sqlalchemy, defusedxml, `packages-deid` | ctms, gateway | ⬜ not yet scaffolded |
| **Tickets** | `apps/tickets` | fastapi, sqlalchemy, sqlmodel, asyncpg, `packages-database` | designer, gateway | ⬜ not yet scaffolded |
| **Payments** | `apps/payments` | fastapi, sqlalchemy, sqlmodel, asyncpg, `packages-database` | gateway only | ⬜ not yet scaffolded |
| **Fileshare** | `apps/fileshare` | fastapi, sqlalchemy, aiosqlite, asyncpg, httpx | gateway only | ⬜ not yet scaffolded |
| **Imaging** | `apps/imaging` | fastapi, sqlalchemy, aiosqlite, asyncpg, pydicom, numpy | gateway only | ⬜ not yet scaffolded |
| *(non-Python edge)* `apps/web` | Vue 3 SPA — clinical portal | Pinia, TailwindCSS, Keycloak OIDC | *(consumer of Gateway)* | out of scope for this pass (JS, not migrated) |
| *(non-Python edge)* `apps/subject-portal` | Vue 3 SPA — patient eCOA/eConsent | Vite, fake-indexeddb | *(consumer of Gateway)* | out of scope for this pass (JS, not migrated) |
| *(non-Python edge)* `packages/gateway-rewrite` | NestJS/TS proxy rewrite | Fastify-style middleware | *(parallel Gateway track)* | out of scope for this pass (already TS, separate initiative) |

`quality*` — Designer coupling from `quality` was seen only in test fixtures, not runtime `*_URL` references; `quality`'s one verified runtime dependency is `execution`.

**Tier 1 shared kernel** (`packages/engine_core`, `packages/security`, `packages/contracts`, `packages/database`, `packages/testing`, `packages/cli`, `packages/deid`, `packages/storage`, `packages/ddf`) underlies every row above and is why the coupling counts in this table only reflect *inter-domain* dependencies — the shared kernel is a prerequisite, not a "domain" edge. `Cadence.Core.Domain`, `Cadence.Security`, `Cadence.Contracts`, `Cadence.Database`, `Cadence.Testing`, and `Cadence.Cli` are already scaffolded and building; `Cadence.Deid`, `Cadence.Storage`, and `Cadence.Ddf` are referenced by the scaffold tooling but have no `.csproj` yet.

---

## 2. Migration Sequence Matrix

Ranked using the three criteria the task specifies — **coupling level** (verified inbound + outbound edge count from §0/§1), **business criticality** (regulatory/operational centrality), and **migration complexity** (persistence engine, native bindings, floating-point/precision remediation already identified in the readiness audit). Phase 0 and the leading edge of Phase 1 are **already executed** in this repository; the rest is the forward plan.

| Phase | Context(s) | Coupling (in+out) | Criticality | Complexity Drivers | Rationale |
| :---: | :--- | :---: | :--- | :--- | :--- |
| **0 — done** | `Cadence.Core.Domain`, `Cadence.Security`, `Cadence.Contracts`, `Cadence.Database`, `Cadence.Testing`, `Cadence.Cli` | n/a (leaf) | Foundational | Low — pure abstractions, no I/O | Every other project depends on these; nothing can compile without them. Already scaffolded and building in `Cadence.sln`. |
| **0 — remaining** | `Cadence.Deid`, `Cadence.Storage`, `Cadence.Ddf` | n/a (leaf) | Foundational | Medium — HIPAA Safe Harbor regex engine, multi-backend storage port, Dataset-JSON ETL | Referenced by `scaffold_dotnet_solution.sh` and consumed by Tier 2/3/4 services (`etmf`, `safety`, `interop` need `Deid`; `fileshare`/`etmf` need `Storage`) but no `.csproj` exists yet — close this gap before Tier 3. |
| **1 — in progress** | `Execution`, `Designer`, `Gateway` | Execution 5in/7out · Designer 1in/2out · Gateway 17out (proxy) | Highest — EDC core of record, protocol authoring source-of-truth, single ingress | High — 35+ table PostgreSQL schema + Merkle audit chain (Execution); Neo4j graph driver + SoA compiler (Designer); YARP dynamic OpenAPI aggregation replacing Python `importlib` reflection per ADR-2239 (Gateway) | Highest fan-in/fan-out and highest business criticality in the platform. `.csproj` already scaffolded for all three; this is the active tracer-bullet slice (see `docs/migration/slices/execution-subjects/`). |
| **2** | `Notifications`, `eTMF` | Notifications 5in/1out · eTMF 5in/2out | High — both are fan-in hubs for 5 other domains each | Medium-High — eTMF needs `Cadence.Deid` + document QC pipeline; Notifications needs MassTransit outbox + MailKit | Second-highest inbound fan-in after Execution/Designer. Migrating these next stabilizes the contracts that `ctms`, `econsent`, `eisf`, `interop`, `safety` all call. |
| **3** | `Ctms`, `Org`, `Econsent`, `Quality`, `Safety` | Ctms 0in/4out · Org 1in/2out · Econsent 1in/3out · Quality 1in/1out · Safety 1in/3out | High — GCP site operations, PI directory/DOA, informed consent, CAPA, pharmacovigilance | High — Safety needs ICH E2B(R3) XML + `Cadence.Deid`; Econsent needs cryptographic signature stamping + Flesch-Kincaid parity vectors; Ctms/Quality are orchestration-heavy with multiple outbound calls that must target either the Tier-1/2 .NET services or their still-Python originals during cutover | Each depends on ≥1 already-migrated Tier 1/2 service (Ctms→Execution/eTMF, Org→eisf's future replacement, Econsent→eTMF, Quality→Execution, Safety→Execution/Notifications), so their outbound calls have a stable target by this phase. `Quality` is explicitly kept out of Tier 1 per Readiness Audit Gap 4 (its `QualityExecutionClient` HTTP dependency on Execution was previously mis-scoped as a leaf node). |
| **3** | `eISF` | 1in/1out (single dependency on eTMF) | Medium — site regulatory binder, low fan-in | Low-Medium | Single clean dependency on eTMF; sequence immediately after eTMF lands in Phase 2. |
| **4 — parallelizable now** | `AiGateway`, `Fileshare`, `Imaging`, `Knowledge`, `Payments` | **0 verified outbound domain calls** (AiGateway is called by 4 domains but calls none; Fileshare/Imaging/Payments have zero outbound *and* zero inbound domain calls besides the Gateway proxy) | Medium-High for AiGateway/Payments (PHI air-gap, IRS 1099/decimal precision); Medium for Fileshare/Imaging/Knowledge | Fileshare needs `IMediaTranscoder`/`IAntiVirusScanner` ports (ffmpeg/clamd, Readiness Audit Gap 5); Imaging needs `fo-dicom`; Payments needs the Universal Decimal Precision Standard (`System.Decimal`, ADR-2239) applied to what is currently Python `float` (Readiness Audit Gap 1) | **These are true topological leaf nodes** — the coupling-graph evidence in §0 shows zero outbound service dependencies, so nothing blocks migrating them in parallel with Phase 2/3 rather than waiting until last. They were placed in "Tier 4 / edge" in the existing matrix by domain category, not by dependency order; this phase reclassifies them by actual graph position. Recommend running this phase as a parallel workstream once Tier 1 remainder (Deid/Storage/Ddf) lands, rather than serially after Phase 3. |
| **5** | `Interop` | 1in/3out | Medium — FHIR/sensor ingestion, not on the critical regulatory path | Medium — SMART on FHIR OAuth2, biometric streaming | Depends on AiGateway/Designer/Notifications; sequence after Phase 2/4 so its outbound targets are stable. |
| **6** | `Tickets` | 1in/1out | Medium — discrepancy/SLA hub | Low-Medium | Single dependency on Knowledge (Phase 4); safe to close out once Knowledge lands. |
| **7** | `Compliance` (`packages/compliance`) + strangler-fig cutover | n/a | Governance | Low (mostly test/RTM tooling) | IQ/OQ/PQ runner and RTM sync validate the full cutover; last because it certifies every prior phase, not because it's technically blocked. |

**Net change from the existing `BOUNDED_CONTEXT_MIGRATION_MATRIX.md` four-tier model:** the tier structure and the Tier-1/Tier-2 tracer bullet ordering are confirmed correct by the runtime evidence. The one material addition is **Phase 4** — `AiGateway`, `Fileshare`, `Imaging`, `Knowledge`, and `Payments` were categorized by domain ("edge integrations") in the prior matrix, which reads as "migrate last." The verified dependency graph shows they have no outbound coupling on any other business domain, so there is no technical reason to defer them behind Phase 3 — they can be staffed as a parallel track as soon as the Tier 1 remainder (`Deid`, `Storage`, `Ddf`) exists, shortening the critical path.

---

## 3. .NET 10 CLI Scaffolding Script

The repository already has an idempotent, heredoc-based generator (`scripts/scaffold_dotnet_solution.sh`) that produces the exact same project layout using `dotnet sln add`. The script below is the `dotnet new`-template-driven equivalent requested for this deliverable: it is safe to run from a clean checkout, only touches projects that don't already exist, and scaffolds the three still-missing Tier 1 packages (`Deid`, `Storage`, `Ddf`) plus the fifteen still-unscaffolded app services from §1/§2, wiring project references and common CPM-managed package additions as it goes.

```bash
#!/usr/bin/env bash
# ==============================================================================
# Cadence Clinical — .NET 10 CLI Scaffold (dotnet new / dotnet add reference)
# Idempotent: skips any project that already has a .csproj on disk.
# Package *versions* are centrally managed in Directory.Packages.props (CPM),
# so `dotnet add package` calls below intentionally omit -v/--version.
# ==============================================================================
set -euo pipefail
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

command -v dotnet >/dev/null 2>&1 || { echo "dotnet SDK not found on PATH" >&2; exit 1; }

SLN="Cadence.sln"
[ -f "$SLN" ] || dotnet new sln -n Cadence

new_classlib() {
  # $1 = folder (packages/<name>/src)  $2 = project name (Cadence.X)  $3 = solution folder
  local dir="$1" proj="$2" folder="$3"
  local csproj="$dir/$proj.csproj"
  if [ -f "$csproj" ]; then
    echo "skip (exists): $csproj"
    return
  fi
  mkdir -p "$dir"
  dotnet new classlib -n "$proj" -o "$dir" --no-restore
  # dotnet new classlib scaffolds Class1.cs; remove the template stub.
  rm -f "$dir/Class1.cs"
  dotnet sln "$SLN" add "$csproj" --solution-folder "$folder"
}

new_webapi() {
  # $1 = folder (apps/<name>/src)  $2 = project name (Cadence.Apps.X)
  local dir="$1" proj="$2"
  local csproj="$dir/$proj.csproj"
  if [ -f "$csproj" ]; then
    echo "skip (exists): $csproj"
    return
  fi
  mkdir -p "$dir"/{Domain,Application,Infrastructure,Endpoints}
  dotnet new webapi -n "$proj" -o "$dir" --no-restore --use-minimal-apis
  dotnet sln "$SLN" add "$csproj" --solution-folder "Apps"
  # Tier 1 shared-kernel references every service needs.
  dotnet add "$csproj" reference packages/engine_core/src/Cadence.Core.Domain.csproj
  dotnet add "$csproj" reference packages/security/src/Cadence.Security.csproj
  dotnet add "$csproj" reference packages/contracts/src/Cadence.Contracts.csproj
  dotnet add "$csproj" reference packages/database/src/Cadence.Database.csproj
  # Common CPM-managed packages (version resolved from Directory.Packages.props).
  dotnet add "$csproj" package Microsoft.AspNetCore.OpenApi
  dotnet add "$csproj" package Scalar.AspNetCore
  dotnet add "$csproj" package Npgsql.EntityFrameworkCore.PostgreSQL
  dotnet add "$csproj" package Microsoft.EntityFrameworkCore
  dotnet add "$csproj" package FluentValidation
}

# ------------------------------------------------------------------------------
# Tier 1 remainder — leaf packages referenced by the scaffold but not yet built.
# ------------------------------------------------------------------------------
new_classlib "packages/deid/src"    "Cadence.Deid"    "Packages"
new_classlib "packages/storage/src" "Cadence.Storage" "Packages"
new_classlib "packages/ddf/src"     "Cadence.Ddf"     "Packages"

# ------------------------------------------------------------------------------
# Phase 4 (parallelizable leaf domains — zero verified outbound coupling)
# ------------------------------------------------------------------------------
new_webapi "apps/ai_gateway/src" "Cadence.Apps.AiGateway"
new_webapi "apps/fileshare/src"  "Cadence.Apps.Fileshare"
new_webapi "apps/imaging/src"    "Cadence.Apps.Imaging"
new_webapi "apps/knowledge/src"  "Cadence.Apps.Knowledge"
new_webapi "apps/payments/src"   "Cadence.Apps.Payments"

# ------------------------------------------------------------------------------
# Phase 2 — fan-in hubs
# ------------------------------------------------------------------------------
new_webapi "apps/notifications/src" "Cadence.Apps.Notifications"
new_webapi "apps/etmf/src"          "Cadence.Apps.Etmf"

# ------------------------------------------------------------------------------
# Phase 3 — orchestrators & regulatory services
# ------------------------------------------------------------------------------
new_webapi "apps/ctms/src"     "Cadence.Apps.Ctms"
new_webapi "apps/org/src"      "Cadence.Apps.Org"
new_webapi "apps/econsent/src" "Cadence.Apps.Econsent"
new_webapi "apps/quality/src"  "Cadence.Apps.Quality"
new_webapi "apps/safety/src"   "Cadence.Apps.Safety"
new_webapi "apps/eisf/src"     "Cadence.Apps.Eisf"

# ------------------------------------------------------------------------------
# Phase 5-6 — remaining edge/orchestration services
# ------------------------------------------------------------------------------
new_webapi "apps/interop/src" "Cadence.Apps.Interop"
new_webapi "apps/tickets/src" "Cadence.Apps.Tickets"

# ------------------------------------------------------------------------------
# Test project scaffold (xunit.v3 via CPM) — skipped if already present.
# ------------------------------------------------------------------------------
if [ ! -f "tests/Cadence.UnitTests.csproj" ]; then
  dotnet new xunit -n Cadence.UnitTests -o tests --no-restore
  dotnet sln "$SLN" add tests/Cadence.UnitTests.csproj --solution-folder Tests
  dotnet add tests/Cadence.UnitTests.csproj package FluentAssertions
  dotnet add tests/Cadence.UnitTests.csproj package NSubstitute
  dotnet add tests/Cadence.UnitTests.csproj package Testcontainers.PostgreSql
  dotnet add tests/Cadence.UnitTests.csproj reference packages/testing/src/Cadence.Testing.csproj
fi

echo "Restoring and verifying full solution build..."
dotnet restore "$SLN"
dotnet build "$SLN" --no-incremental

echo "Scaffold complete."
```

**Notes on running this against the current checkout:** every project this script would create for `Cadence.Core.Domain`, `Cadence.Security`, `Cadence.Contracts`, `Cadence.Database`, `Cadence.Testing`, `Cadence.Cli`, `Cadence.Apps.Execution`, `Cadence.Apps.Designer`, and `Cadence.Apps.Gateway` already exists and is skipped by the existence checks — this script only fills the remaining gap (3 Tier 1 packages + 15 app services) identified in §1. Its output is the `dotnet new`-idiomatic counterpart to `scripts/scaffold_dotnet_solution.sh`; either can be used, but do not run both against the same uncommitted tree without reviewing the diff, since `dotnet new webapi` emits a different starter `Program.cs` than the heredoc-based generator.
