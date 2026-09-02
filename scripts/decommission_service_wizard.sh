#!/usr/bin/env bash
# ==============================================================================
# Cadence Clinical Platform - Interactive Service Decommissioning Wizard (POSIX)
# Requirements: PRD-SYS-053, ADR-2233
# ==============================================================================

set -euo pipefail

SERVICE_NAME="${1:-}"

# Formatting
if [[ -t 1 ]] && command -v tput >/dev/null 2>&1 && [[ "$(tput colors 2>/dev/null || echo 0)" -ge 8 ]]; then
  BOLD=$(tput bold); DIM=$(tput dim); RESET=$(tput sgr0)
  BLUE=$(tput setaf 4); GREEN=$(tput setaf 2); YELLOW=$(tput setaf 3); RED=$(tput setaf 1); CYAN=$(tput setaf 6)
else
  BOLD=""; DIM=""; RESET=""; BLUE=""; GREEN=""; YELLOW=""; RED=""; CYAN=""
fi

_clear() {
  [[ -t 1 ]] || return 0
  if command -v tput >/dev/null 2>&1; then tput clear; else printf '\033[2J\033[3J\033[H'; fi
}

_clear
printf '\n%s%s================================================================================%s\n' "$BOLD" "$RED" "$RESET"
printf '%s%s   CADENCE CLINICAL PLATFORM — SERVICE DECOMMISSIONING WIZARD                  %s\n' "$BOLD" "$RED" "$RESET"
printf '%s%s================================================================================%s\n' "$BOLD" "$RED" "$RESET"
printf '%s  Process: Decommissioning Legacy Python Implementations ("Clearing Out the Old")%s\n' "$DIM" "$RESET"
printf '%s  Regulatory Standard: 21 CFR Part 11 / Change Control (PRD-SYS-053)%s\n\n' "$DIM" "$RESET"

if [ -z "$SERVICE_NAME" ]; then
  read -p "Enter service name to decommission (e.g. 'execution'): " SERVICE_NAME || true
fi

if [ -z "$SERVICE_NAME" ]; then
  echo "Error: Service name is required."
  exit 1
fi

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SERVICE_DIR="$REPO_ROOT/apps/$SERVICE_NAME"
PY_SRC="$SERVICE_DIR/src/cadence/$SERVICE_NAME"

# Step 1: Check .NET 10 project exists
printf '%s[1/4] Verifying .NET 10 Target Implementation...%s\n' "$YELLOW" "$RESET"
if ! compgen -G "$SERVICE_DIR/src/*.csproj" > /dev/null; then
  printf '  %s[ABORT] No .NET 10 .csproj found in %s/src. Scaffolding must precede decommissioning!%s\n' "$RED" "$SERVICE_DIR" "$RESET"
  exit 1
fi
printf '  %s[OK] Active .NET 10 project found.%s\n' "$GREEN" "$RESET"

# Step 2: Parity Confirmation
printf '\n%s[2/4] Behavioral Parity & Contract Confirmation...%s\n' "$YELLOW" "$RESET"
read -p "Confirm all .NET 10 unit tests pass with 100% GxP requirement traceability? [y/N]: " CONFIRM_TESTS || true
if [[ ! "$CONFIRM_TESTS" =~ ^[yY] ]]; then
  echo "Decommissioning aborted."
  exit 0
fi

# Step 3: Python Source Purge
printf '\n%s[3/4] Legacy Python Source Pruning...%s\n' "$YELLOW" "$RESET"
if [ -d "$PY_SRC" ]; then
  printf '  Legacy source directory: %s%s%s\n' "$RED" "$PY_SRC" "$RESET"
  read -p "Type 'DELETE' to confirm permanent removal of legacy Python code: " PURGE_CONFIRM || true
  if [ "$PURGE_CONFIRM" = "DELETE" ]; then
    rm -rf "$PY_SRC"
    printf '  %s[SUCCESS] Pruned legacy Python source directory.%s\n' "$GREEN" "$RESET"
  else
    echo "  [SKIPPED] Legacy Python directory was not deleted."
  fi
else
  printf '  %s[OK] Legacy Python directory already removed.%s\n' "$GREEN" "$RESET"
fi

# Step 4: Verification
printf '\n%s[4/4] ADR and GxP Compliance Synchronization...%s\n' "$YELLOW" "$RESET"
python "$REPO_ROOT/scripts/validate_adrs.py"
python "$REPO_ROOT/scripts/sync_gxp.py"

printf '\n%s%s================================================================================%s\n' "$BOLD" "$GREEN" "$RESET"
printf '%s%s   DECOMMISSIONING COMPLETE FOR %s                                             %s\n' "$BOLD" "$GREEN" "$SERVICE_NAME" "$RESET"
printf '%s%s================================================================================%s\n\n' "$BOLD" "$GREEN" "$RESET"

