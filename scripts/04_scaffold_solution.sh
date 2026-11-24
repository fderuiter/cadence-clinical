#!/usr/bin/env bash
# ==============================================================================
# Cadence Clinical — .NET 10 Solution Scaffold (Phase 0 baseline)
#
# Idempotent: every project-creation step first checks for an existing
# .csproj and skips it if found, so this is safe to run against the
# current checkout (which already has 9 Tier-0 packages, 3 app services,
# and 2 test projects scaffolded — see docs/migration/phase0/05_orphan_audit_gate.md
# §4 for exactly what already exists and what this script adds).
#
# This script does NOT write application domain code. It only creates
# project skeletons (Domain/Application/Infrastructure/Endpoints folders),
# wires ProjectReferences for the Tier-0 shared kernel, and adds the
# common CPM-managed NuGet packages every Web API service needs. Business
# logic transcription is explicitly out of scope for Phase 0.
#
# NOTE ON VERIFICATION: this script was authored and reviewed but could
# NOT be executed end-to-end in the environment that produced this Phase 0
# baseline — the .NET 10 SDK could not be installed there (see
# docs/migration/phase0/01_system_topology.md §6). Before this script is
# treated as verified, it must be run once in an environment with the
# .NET 10 SDK on PATH and `dotnet build Cadence.sln` confirmed to return
# zero warnings / zero errors under this repo's TreatWarningsAsErrors=true
# policy (Directory.Build.props). Do not mark Phase 0 "build-verified"
# on the basis of this script's text alone.
# ==============================================================================
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

BOLD="\033[1m"; GREEN="\033[0;32m"; YELLOW="\033[1;33m"; CYAN="\033[0;36m"; RED="\033[0;31m"; RESET="\033[0m"
log()  { printf "%b%s%b\n" "$CYAN" "$1" "$RESET"; }
ok()   { printf "  %b[ok]%b %s\n" "$GREEN" "$RESET" "$1"; }
skip() { printf "  %b[skip]%b %s (already exists)\n" "$YELLOW" "$RESET" "$1"; }

printf "\n%b================================================================================%b\n" "$CYAN$BOLD" "$RESET"
printf "%b   CADENCE CLINICAL — .NET 10 PHASE 0 SOLUTION SCAFFOLD                          %b\n" "$CYAN$BOLD" "$RESET"
printf "%b================================================================================%b\n\n" "$CYAN$BOLD" "$RESET"

# ------------------------------------------------------------------------------
# 1. Toolchain check
# ------------------------------------------------------------------------------
log "[1/6] Verifying .NET 10 SDK on PATH..."
if ! command -v dotnet >/dev/null 2>&1; then
    printf "  %b[ERROR] dotnet SDK not found on PATH.%b\n" "$RED" "$RESET"
    printf "  This repo pins SDK 10.0.100 in global.json with rollForward=latestFeature.\n"
    printf "  Install it, or run this script inside docker/Dockerfile.dotnet, then re-run.\n"
    exit 1
fi
DOTNET_VERSION="$(dotnet --version)"
ok "dotnet $DOTNET_VERSION"

# ------------------------------------------------------------------------------
# 2. Central build configuration (must already exist — this script verifies,
#    it does not overwrite, since Directory.Build.props/Directory.Packages.props
#    already encode the platform's TreatWarningsAsErrors/CPM/analyzer policy).
# ------------------------------------------------------------------------------
log "\n[2/6] Verifying Directory.Build.props / Directory.Packages.props / global.json..."
for f in Directory.Build.props Directory.Packages.props global.json .editorconfig; do
    if [ ! -f "$f" ]; then
        printf "  %b[ERROR] %s is missing — this is a Phase-0-established baseline file and must exist before scaffolding proceeds.%b\n" "$RED" "$f" "$RESET"
        exit 1
    fi
    ok "$f present"
done

# ------------------------------------------------------------------------------
# 3. Solution file
# ------------------------------------------------------------------------------
log "\n[3/6] Ensuring Cadence.sln exists..."
SLN="Cadence.sln"
if [ ! -f "$SLN" ]; then
    dotnet new sln -n Cadence
    ok "created $SLN"
else
    skip "$SLN"
fi

# ------------------------------------------------------------------------------
# 4. Tier 0 — shared kernel class libraries
# ------------------------------------------------------------------------------
log "\n[4/6] Scaffolding Tier 0 shared-kernel packages..."

new_classlib() {
  local dir="$1" proj="$2" folder="$3"
  local csproj="$dir/$proj.csproj"
  if [ -f "$csproj" ]; then
    skip "$csproj"
    return
  fi
  mkdir -p "$dir"
  dotnet new classlib -n "$proj" -o "$dir" --no-restore
  rm -f "$dir/Class1.cs"
  dotnet sln "$SLN" add "$csproj" --solution-folder "$folder"
  ok "scaffolded $csproj"
}

# Already present in the repo checkout that produced this baseline:
new_classlib "packages/engine_core/src" "Cadence.Core.Domain" "Packages"
new_classlib "packages/security/src"    "Cadence.Security"    "Packages"
new_classlib "packages/database/src"    "Cadence.Database"    "Packages"
new_classlib "packages/contracts/src"   "Cadence.Contracts"   "Packages"
new_classlib "packages/testing/src"     "Cadence.Testing"     "Packages"
new_classlib "packages/cli/src"         "Cadence.Cli"         "Packages"
# Referenced by scripts/scaffold_dotnet_solution.sh and the Phase 0 domain
# decomposition deliverable, but not yet built as of this baseline:
new_classlib "packages/deid/src"        "Cadence.Deid"        "Packages"
new_classlib "packages/storage/src"     "Cadence.Storage"     "Packages"
new_classlib "packages/ddf/src"         "Cadence.Ddf"         "Packages"

# ------------------------------------------------------------------------------
# 5. Tier 2-4 — application services (Minimal API webapi projects)
# ------------------------------------------------------------------------------
log "\n[5/6] Scaffolding application service projects (Single-Assembly Clean Architecture)..."

new_webapi() {
  local dir="$1" proj="$2"
  local csproj="$dir/$proj.csproj"
  if [ -f "$csproj" ]; then
    skip "$csproj"
    return
  fi
  mkdir -p "$dir"/{Domain,Application,Infrastructure,Endpoints}
  dotnet new webapi -n "$proj" -o "$dir" --no-restore --use-minimal-apis
  dotnet sln "$SLN" add "$csproj" --solution-folder "Apps"
  dotnet add "$csproj" reference packages/engine_core/src/Cadence.Core.Domain.csproj
  dotnet add "$csproj" reference packages/security/src/Cadence.Security.csproj
  dotnet add "$csproj" reference packages/contracts/src/Cadence.Contracts.csproj
  dotnet add "$csproj" reference packages/database/src/Cadence.Database.csproj
  dotnet add "$csproj" package Microsoft.AspNetCore.OpenApi
  dotnet add "$csproj" package Scalar.AspNetCore
  dotnet add "$csproj" package Npgsql.EntityFrameworkCore.PostgreSQL
  dotnet add "$csproj" package Microsoft.EntityFrameworkCore
  dotnet add "$csproj" package FluentValidation
  ok "scaffolded $csproj"
}

# Order follows docs/migration/phase0/03_migration_plan.md §2 (Tier 2 -> 4).
# Already present in the repo checkout that produced this baseline:
new_webapi "apps/execution/src" "Cadence.Apps.Execution"
new_webapi "apps/designer/src"  "Cadence.Apps.Designer"
new_webapi "apps/gateway/src"   "Cadence.Apps.Gateway"

# Tier 2 remainder — blocked on MIG-05 (Execution/Org shared-DB split) per
# 03_migration_plan.md; the project skeleton is scaffolded now regardless,
# since the blocker is a data/architecture decision, not a build blocker.
new_webapi "apps/org/src" "Cadence.Apps.Org"

# Tier 3 — orchestrators & regulatory services, in the sequencing order
# from 03_migration_plan.md §2 (Quality and Payments first: narrowest
# dependency footprint / zero coupling respectively).
new_webapi "apps/quality/src"   "Cadence.Apps.Quality"
new_webapi "apps/payments/src"  "Cadence.Apps.Payments"
new_webapi "apps/ctms/src"      "Cadence.Apps.Ctms"
new_webapi "apps/etmf/src"      "Cadence.Apps.Etmf"
new_webapi "apps/safety/src"    "Cadence.Apps.Safety"
new_webapi "apps/econsent/src"  "Cadence.Apps.Econsent"
new_webapi "apps/eisf/src"      "Cadence.Apps.Eisf"
new_webapi "apps/tickets/src"   "Cadence.Apps.Tickets"

# Tier 4 — fan-in hubs, sinks, and the SCE-sandbox-bearing Interop service
# (Interop scaffolded last per 03_migration_plan.md's MIG-08 blocker).
new_webapi "apps/notifications/src" "Cadence.Apps.Notifications"
new_webapi "apps/ai_gateway/src"    "Cadence.Apps.AiGateway"
new_webapi "apps/fileshare/src"     "Cadence.Apps.Fileshare"
new_webapi "apps/imaging/src"       "Cadence.Apps.Imaging"
new_webapi "apps/knowledge/src"     "Cadence.Apps.Knowledge"
new_webapi "apps/interop/src"       "Cadence.Apps.Interop"

# ------------------------------------------------------------------------------
# 5b. Test projects
# ------------------------------------------------------------------------------
log "\n[5b] Ensuring test projects exist..."
if [ ! -f "tests/Cadence.UnitTests.csproj" ]; then
  dotnet new xunit -n Cadence.UnitTests -o tests --no-restore
  dotnet sln "$SLN" add tests/Cadence.UnitTests.csproj --solution-folder Tests
  dotnet add tests/Cadence.UnitTests.csproj package FluentAssertions
  dotnet add tests/Cadence.UnitTests.csproj package NSubstitute
  dotnet add tests/Cadence.UnitTests.csproj package Testcontainers.PostgreSql
  dotnet add tests/Cadence.UnitTests.csproj reference packages/testing/src/Cadence.Testing.csproj
  ok "scaffolded tests/Cadence.UnitTests.csproj"
else
  skip "tests/Cadence.UnitTests.csproj"
fi
if [ ! -f "tests/Cadence.ArchitectureTests.csproj" ]; then
  dotnet new xunit -n Cadence.ArchitectureTests -o tests --no-restore
  dotnet sln "$SLN" add tests/Cadence.ArchitectureTests.csproj --solution-folder Tests
  dotnet add tests/Cadence.ArchitectureTests.csproj package ArchUnitNET.xUnit
  ok "scaffolded tests/Cadence.ArchitectureTests.csproj"
else
  skip "tests/Cadence.ArchitectureTests.csproj"
fi

# ------------------------------------------------------------------------------
# 6. Restore & build gate
# ------------------------------------------------------------------------------
log "\n[6/6] Restoring and building the full solution..."
dotnet restore "$SLN"
dotnet build "$SLN" --no-incremental

printf "\n%b================================================================================%b\n" "$GREEN$BOLD" "$RESET"
printf "%b   SCAFFOLD COMPLETE — Cadence.sln builds under TreatWarningsAsErrors=true.       %b\n" "$GREEN$BOLD" "$RESET"
printf "%b================================================================================%b\n\n" "$GREEN$BOLD" "$RESET"
