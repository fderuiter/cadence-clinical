#!/usr/bin/env bash
# ==============================================================================
# Cadence Clinical Platform - Interactive .NET 10 Developer Onboarding Wizard (POSIX)
# Requirements: PRD-SYS-053, ADR-2233
# ==============================================================================

set -euo pipefail

TOTAL_STAGES=6
_STAGE_INDEX=0

# Formatting
if [[ -t 1 ]] && command -v tput >/dev/null 2>&1 && [[ "$(tput colors 2>/dev/null || echo 0)" -ge 8 ]]; then
  BOLD=$(tput bold); DIM=$(tput dim); RESET=$(tput sgr0)
  GREEN=$(tput setaf 2); YELLOW=$(tput setaf 3); CYAN=$(tput setaf 6)
else
  BOLD=""; DIM=""; RESET=""; GREEN=""; YELLOW=""; CYAN=""
fi

_clear() {
  [[ -t 1 ]] || return 0
  if command -v tput >/dev/null 2>&1; then tput clear; else printf '\033[2J\033[3J\033[H'; fi
}

stage() {
  _STAGE_INDEX=$(( _STAGE_INDEX + 1 ))
  printf '\n%s%s[%d/%d] %s%s\n' "$BOLD" "$YELLOW" "$_STAGE_INDEX" "$TOTAL_STAGES" "$1" "$RESET"
  printf '%s  %s%s\n' "$DIM" "$2" "$RESET"
  printf '%s  ------------------------------------------------------------------------------%s\n' "$DIM" "$RESET"
}

_clear
printf '\n%s%s================================================================================%s\n' "$BOLD" "$CYAN" "$RESET"
printf '%s%s   CADENCE CLINICAL PLATFORM — .NET 10 DEVELOPER ONBOARDING WIZARD              %s\n' "$BOLD" "$CYAN" "$RESET"
printf '%s%s================================================================================%s\n' "$BOLD" "$CYAN" "$RESET"
printf '%s  Target Architecture: .NET 10 (C# 14 / ASP.NET Core 10 Minimal APIs)%s\n' "$DIM" "$RESET"
printf '%s  Regulatory Standard: 21 CFR Part 11 / ICH GCP E6 (PRD-SYS-053)%s\n\n' "$DIM" "$RESET"

read -r -p "Ready to begin setup? [Y/n]: " START_PROMPT || true
if [[ "$START_PROMPT" =~ ^[nN] ]]; then
  echo "Onboarding paused. Re-run anytime."
  exit 0
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

# Stage 1: .NET 10 SDK
stage ".NET 10 SDK Environment" "Validating .NET 10 SDK installation via global.json..."
SDK_FOUND=false
if command -v dotnet >/dev/null 2>&1; then
  CURRENT_VERSION=$( (cd "$REPO_ROOT" && dotnet --version) 2>/dev/null || true )
  if [[ "$CURRENT_VERSION" =~ ^10\. ]]; then
    printf '  %s[SUCCESS] Found active .NET 10 SDK: %s%s\n' "$GREEN" "$CURRENT_VERSION" "$RESET"
    SDK_FOUND=true
  fi
fi

if [ "$SDK_FOUND" = false ]; then
  read -r -p "  Download and install .NET 10 SDK automatically? [Y/n]: " INSTALL_SDK || true
  if [[ ! "$INSTALL_SDK" =~ ^[nN] ]]; then
    if ! bash "$SCRIPT_DIR/setup_dotnet_env.sh"; then
      printf '  %s[ERROR] Environment setup failed. Aborting onboarding wizard.%s\n' "$YELLOW" "$RESET" >&2
      exit 1
    fi
    CADENCE_RESOLVE_REPO_ROOT="$REPO_ROOT"
    # shellcheck source=scripts/resolve_dotnet_env.sh
    . "$SCRIPT_DIR/resolve_dotnet_env.sh"
    unset CADENCE_RESOLVE_REPO_ROOT
  fi
fi

# Stage 2: Global Tools
stage "Global Developer Tools" "Ensuring required dotnet CLI tools are installed..."
for tool in dotnet-ef dotnet-format; do
  dotnet tool update --global "$tool" 2>/dev/null || dotnet tool install --global "$tool" 2>/dev/null || true
  printf '  %s[SUCCESS] %s is configured.%s\n' "$GREEN" "$tool" "$RESET"
done

# Stage 3: HTTPS Dev Certs
stage "HTTPS Dev Certificates" "Configuring local development certificates..."
dotnet dev-certs https --clean 2>/dev/null || true
dotnet dev-certs https --trust 2>/dev/null || true
printf '  %s[SUCCESS] Dev certificates trusted.%s\n' "$GREEN" "$RESET"

# Stage 4: Datastores
stage "Clinical Datastores" "Probing PostgreSQL (5432)..."
if nc -z 127.0.0.1 5432 2>/dev/null; then
  printf '  %s[SUCCESS] PostgreSQL running on port 5432.%s\n' "$GREEN" "$RESET"
else
  printf '  %s[INFO] PostgreSQL not reachable. Start via: docker compose -f docker/docker-compose.yml up -d postgres%s\n' "$YELLOW" "$RESET"
fi


# Stage 5: Environment
stage "Environment Configuration (.env)" "Ensuring local .env is initialized..."
REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
if [ ! -f "$REPO_ROOT/.env" ] && [ -f "$REPO_ROOT/.env.example" ]; then
  cp "$REPO_ROOT/.env.example" "$REPO_ROOT/.env"
  printf '  %s[SUCCESS] Initialized .env from template.%s\n' "$GREEN" "$RESET"
else
  printf '  %s[SUCCESS] .env file is present.%s\n' "$GREEN" "$RESET"
fi

# Stage 6: Validation
stage "Solution Validation" "Executing ADR validation..."
dotnet run --project "$REPO_ROOT/packages/cli/src/Cadence.Cli.csproj" -- check adrs

printf '\n%s%s================================================================================%s\n' "$BOLD" "$GREEN" "$RESET"
printf '%s%s   ONBOARDING COMPLETE! Your Cadence .NET 10 environment is ready.              %s\n' "$BOLD" "$GREEN" "$RESET"
printf '%s%s================================================================================%s\n\n' "$BOLD" "$GREEN" "$RESET"

