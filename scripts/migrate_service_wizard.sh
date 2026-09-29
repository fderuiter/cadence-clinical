#!/usr/bin/env bash
# ==============================================================================
# Cadence Clinical Platform - Interactive Microservice Migration Wizard (POSIX)
# Requirements: PRD-SYS-053, ADR-2233, ADR-2234
# ==============================================================================

set -euo pipefail

SERVICE_NAME="${1:-}"

# Formatting
if [[ -t 1 ]] && command -v tput >/dev/null 2>&1 && [[ "$(tput colors 2>/dev/null || echo 0)" -ge 8 ]]; then
  BOLD=$(tput bold); DIM=$(tput dim); RESET=$(tput sgr0)
  BLUE=$(tput setaf 4); GREEN=$(tput setaf 2); YELLOW=$(tput setaf 3); CYAN=$(tput setaf 6)
else
  BOLD=""; DIM=""; RESET=""; BLUE=""; GREEN=""; YELLOW=""; CYAN=""
fi

_clear() {
  [[ -t 1 ]] || return 0
  if command -v tput >/dev/null 2>&1; then tput clear; else printf '\033[2J\033[3J\033[H'; fi
}

_clear
printf '\n%s%s================================================================================%s\n' "$BOLD" "$CYAN" "$RESET"
printf '%s%s   CADENCE CLINICAL PLATFORM — MICROSERVICE TRANSCRIPTION WIZARD                %s\n' "$BOLD" "$CYAN" "$RESET"
printf '%s%s================================================================================%s\n' "$BOLD" "$CYAN" "$RESET"
printf '%s  Architecture: Single-Assembly Clean Architecture (.NET 10 / C# 14)%s\n' "$DIM" "$RESET"
printf '%s  GxP Standard: 21 CFR Part 11 / Merkle Interceptor (PRD-SYS-053)%s\n\n' "$DIM" "$RESET"

if [ -z "$SERVICE_NAME" ]; then
  printf '%sAvailable Clinical Services:%s\n' "$YELLOW" "$RESET"
  echo "  [1] execution      (EDC Clinical Observation Engine)"
  echo "  [2] designer       (MDR Protocol Designer - Neo4j)"
  echo "  [3] ctms           (Clinical Trial Management System)"
  echo "  [4] etmf           (Electronic Trial Master File)"
  echo "  [5] eisf           (Investigator Site File)"
  echo "  [6] quality        (CAPA & Deviation Tracking)"
  echo "  [7] safety         (SUSAR & Safety Reporting)"
  echo "  [8] interop        (FHIR & CDISC ODM Connectors)"
  echo "  [9] notifications  (Clinical Notification Dispatch)"
  echo "  [10] econsent      (Electronic Consent Pipeline)"
  echo ""
  read -p "Enter service name or number [1-10]: " SELECTION || true
  case "$SELECTION" in
    1) SERVICE_NAME="execution" ;;
    2) SERVICE_NAME="designer" ;;
    3) SERVICE_NAME="ctms" ;;
    4) SERVICE_NAME="etmf" ;;
    5) SERVICE_NAME="eisf" ;;
    6) SERVICE_NAME="quality" ;;
    7) SERVICE_NAME="safety" ;;
    8) SERVICE_NAME="interop" ;;
    9) SERVICE_NAME="notifications" ;;
    10) SERVICE_NAME="econsent" ;;
    *) SERVICE_NAME=$(echo "$SELECTION" | tr '[:upper:]' '[:lower:]' | xargs) ;;
  esac
fi

if [ -z "$SERVICE_NAME" ]; then
  echo "Error: Service name is required."
  exit 1
fi

CAP_NAME="$(tr '[:lower:]' '[:upper:]' <<< "${SERVICE_NAME:0:1}")${SERVICE_NAME:1}"
REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SRC_DIR="$REPO_ROOT/apps/$SERVICE_NAME/src"

echo ""
printf '%sTarget Service: Cadence.Apps.%s%s\n' "$CYAN" "$CAP_NAME" "$RESET"
printf '%sSource Path:    %s%s\n' "$DIM" "$SRC_DIR" "$RESET"

# 1. Directories
printf '\n%s[1/4] Scaffolding Clean Architecture Layout...%s\n' "$YELLOW" "$RESET"
mkdir -p "$SRC_DIR/Domain" "$SRC_DIR/Application" "$SRC_DIR/Infrastructure" "$SRC_DIR/Endpoints"
printf '  %s[SUCCESS] Created Domain, Application, Infrastructure, and Endpoints directories.%s\n' "$GREEN" "$RESET"

# 2. Csproj
CSPROJ_FILE="$SRC_DIR/Cadence.Apps.$CAP_NAME.csproj"
if [ ! -f "$CSPROJ_FILE" ]; then
  cat <<EOF > "$CSPROJ_FILE"
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <RootNamespace>Cadence.Apps.$CAP_NAME</RootNamespace>
    <Description>Cadence Clinical $CAP_NAME Service (.NET 10 / C# 14 Minimal APIs).</Description>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\..\packages\engine_core\src\Cadence.Core.Domain.csproj" />
    <ProjectReference Include="..\..\..\packages\security\src\Cadence.Security.csproj" />
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
EOF
  printf '  %s[SUCCESS] Created project file: Cadence.Apps.%s.csproj%s\n' "$GREEN" "$CAP_NAME" "$RESET"
fi

# 3. Check Solution
printf '\n%s[2/4] Checking Cadence.sln Solution Binding...%s\n' "$YELLOW" "$RESET"
if grep -q "Cadence.Apps.$CAP_NAME.csproj" "$REPO_ROOT/Cadence.sln"; then
  printf '  %s[SUCCESS] Project is bound in Cadence.sln.%s\n' "$GREEN" "$RESET"
else
  printf '  %s[NOTICE] Run: dotnet sln Cadence.sln add apps/%s/src/Cadence.Apps.%s.csproj --solution-folder Apps%s\n' "$YELLOW" "$SERVICE_NAME" "$CAP_NAME" "$RESET"
fi

# 4. ADR Validation
printf '\n%s[3/4] Validating ADR Index & Requirements...%s\n' "$YELLOW" "$RESET"
dotnet run --project "$REPO_ROOT/packages/cli/src/Cadence.Cli.csproj" -- check adrs

printf '\n%s%s================================================================================%s\n' "$BOLD" "$GREEN" "$RESET"
printf '%s%s   SCAFFOLDING COMPLETE FOR Cadence.Apps.%s                                       %s\n' "$BOLD" "$GREEN" "$CAP_NAME" "$RESET"
printf '%s%s================================================================================%s\n\n' "$BOLD" "$GREEN" "$RESET"

