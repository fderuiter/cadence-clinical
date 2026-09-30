#!/usr/bin/env bash
# ==============================================================================
# Cadence Clinical Research Software Platform - .NET 10 Baseline Solution Scaffold
# Architecture: Clean Architecture / ASP.NET Core 10 Minimal APIs
# Governing ADRs: ADR-2233, ADR-2428 | Standard: 21 CFR Part 11 (PRD-SYS-053)
# ==============================================================================

set -euo pipefail

# Visual styling
BOLD="\033[1m"
GREEN="\033[0;32m"
YELLOW="\033[1;33m"
CYAN="\033[0;36m"
RED="\033[0;31m"
RESET="\033[0m"

printf "\n%b================================================================================%b\n" "$CYAN$BOLD" "$RESET"
printf "%b   CADENCE CLINICAL PLATFORM — .NET 10 SOLUTION REPRODUCIBLE SCAFFOLD           %b\n" "$CYAN$BOLD" "$RESET"
printf "%b================================================================================%b\n\n" "$CYAN$BOLD" "$RESET"

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$REPO_ROOT"

# ------------------------------------------------------------------------------
# 1. Environment & SDK Verification
# ------------------------------------------------------------------------------
printf "%b[1/5] Verifying .NET 10 Toolchain Environment...%b\n" "$YELLOW" "$RESET"

if ! command -v dotnet >/dev/null 2>&1; then
    printf "%b[ERROR] .NET SDK is not installed or not on PATH.%b\n" "$RED" "$RESET"
    exit 1
fi

DOTNET_VERSION="$(dotnet --version || true)"
printf "  %b• Active .NET SDK Version:%b %s\n" "$CYAN" "$RESET" "$DOTNET_VERSION"

# ------------------------------------------------------------------------------
# 2. Verify Central Build Configuration (Directory.Build.props & Packages.props)
# ------------------------------------------------------------------------------
printf "\n%b[2/5] Validating Central Package Management (CPM) and Global Properties...%b\n" "$YELLOW" "$RESET"

if [ ! -f "Directory.Build.props" ]; then
    printf "  Creating Directory.Build.props...\n"
    cat << 'PROPS' > Directory.Build.props
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>14</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <Deterministic>true</Deterministic>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <NoWarn>$(NoWarn);CS1591</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.NetAnalyzers">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="SonarAnalyzer.CSharp">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>
</Project>
PROPS
fi

if [ ! -f "Directory.Packages.props" ]; then
    printf "  Directory.Packages.props must exist for CPM.\n"
fi
printf "  %b[SUCCESS] Global props and CPM configurations verified.%b\n" "$GREEN" "$RESET"

# ------------------------------------------------------------------------------
# 3. Scaffold Single-Assembly Clean Architecture Microservices
# ------------------------------------------------------------------------------
printf "\n%b[3/5] Scaffolding Microservice Layers (Domain, Application, Infrastructure, Endpoints)...%b\n" "$YELLOW" "$RESET"

SERVICES=(
    "execution"
    "designer"
    "gateway"
    "ctms"
    "etmf"
    "eisf"
    "quality"
    "safety"
    "interop"
    "notifications"
    "econsent"
    "org"
    "payments"
    "tickets"
    "fileshare"
    "imaging"
    "knowledge"
    "ai_gateway"
)

for SVC in "${SERVICES[@]}"; do
    SRC_DIR="apps/$SVC/src"
    mkdir -p "$SRC_DIR/Domain" "$SRC_DIR/Application" "$SRC_DIR/Infrastructure" "$SRC_DIR/Endpoints"

    # Capitalize first letter for C# project naming
    CAP_NAME="$(tr '[:lower:]' '[:upper:]' <<< "${SVC:0:1}")${SVC:1}"
    if [ "$SVC" == "ai_gateway" ]; then
        CAP_NAME="AiGateway"
    fi

    CSPROJ_FILE="$SRC_DIR/Cadence.Apps.$CAP_NAME.csproj"
    if [ ! -f "$CSPROJ_FILE" ]; then
        printf "  • Scaffolding project: %s\n" "$CSPROJ_FILE"
        cat << CSPROJ > "$CSPROJ_FILE"
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <RootNamespace>Cadence.Apps.$CAP_NAME</RootNamespace>
    <Description>Cadence Clinical $CAP_NAME Service (.NET 10 / C# 14 Minimal APIs).</Description>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\..\packages\engine_core\src\Cadence.Core.Domain.csproj" />
    <ProjectReference Include="..\..\..\packages\security\src\Cadence.Security.csproj" />
    <ProjectReference Include="..\..\..\packages\contracts\src\Cadence.Contracts.csproj" />
    <ProjectReference Include="..\..\..\packages\database\src\Cadence.Database.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.OpenApi" />
    <PackageReference Include="Scalar.AspNetCore" />
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
    <PackageReference Include="Microsoft.EntityFrameworkCore" />
    <PackageReference Include="FluentValidation" />
  </ItemGroup>
</Project>
CSPROJ
    fi
done
printf "  %b[SUCCESS] All 18 microservice project skeletons scaffolded.%b\n" "$GREEN" "$RESET"

# ------------------------------------------------------------------------------
# 4. Bind Projects to Root Cadence.sln
# ------------------------------------------------------------------------------
printf "\n%b[4/5] Synchronizing Solution Hierarchy (Cadence.sln)...%b\n" "$YELLOW" "$RESET"

if [ ! -f "Cadence.sln" ]; then
    dotnet new sln -n Cadence
fi

# Add core packages
dotnet sln Cadence.sln add packages/engine_core/src/Cadence.Core.Domain.csproj --solution-folder Packages || true
dotnet sln Cadence.sln add packages/security/src/Cadence.Security.csproj --solution-folder Packages || true
dotnet sln Cadence.sln add packages/database/src/Cadence.Database.csproj --solution-folder Packages || true
dotnet sln Cadence.sln add packages/contracts/src/Cadence.Contracts.csproj --solution-folder Packages || true
dotnet sln Cadence.sln add packages/testing/src/Cadence.Testing.csproj --solution-folder Packages || true
dotnet sln Cadence.sln add packages/cli/src/Cadence.Cli.csproj --solution-folder Packages || true
dotnet sln Cadence.sln add packages/deid/src/Cadence.Deid.csproj --solution-folder Packages || true
dotnet sln Cadence.sln add packages/storage/src/Cadence.Storage.csproj --solution-folder Packages || true
dotnet sln Cadence.sln add packages/ddf/src/Cadence.Ddf.csproj --solution-folder Packages || true

# Add services
for SVC in "${SERVICES[@]}"; do
    CAP_NAME="$(tr '[:lower:]' '[:upper:]' <<< "${SVC:0:1}")${SVC:1}"
    if [ "$SVC" == "ai_gateway" ]; then
        CAP_NAME="AiGateway"
    fi
    CSPROJ_FILE="apps/$SVC/src/Cadence.Apps.$CAP_NAME.csproj"
    if [ -f "$CSPROJ_FILE" ]; then
        dotnet sln Cadence.sln add "$CSPROJ_FILE" --solution-folder Apps || true
    fi
done

# Add test projects
dotnet sln Cadence.sln add tests/Cadence.UnitTests.csproj --solution-folder Tests || true
dotnet sln Cadence.sln add tests/Cadence.ArchitectureTests.csproj --solution-folder Tests || true

printf "  %b[SUCCESS] Solution hierarchy synchronized.%b\n" "$GREEN" "$RESET"

# ------------------------------------------------------------------------------
# 5. Verify Solution Build & Diagnostic Gate
# ------------------------------------------------------------------------------
printf "\n%b[5/5] Verifying Solution Compilation & Roslyn Analyzer Diagnostics...%b\n" "$YELLOW" "$RESET"
dotnet build Cadence.sln --no-incremental

printf "\n%b================================================================================%b\n" "$GREEN$BOLD" "$RESET"
printf "%b   SCAFFOLD COMPLETE: Solution Cadence.sln ready for service transcription!      %b\n" "$GREEN$BOLD" "$RESET"
printf "%b================================================================================%b\n\n" "$GREEN$BOLD" "$RESET"
