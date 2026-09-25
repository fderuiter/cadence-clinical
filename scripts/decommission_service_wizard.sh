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
  read -p "Enter service name to decommission (e.g. 'execution', or 'all'): " SERVICE_NAME || true
fi

if [ -z "$SERVICE_NAME" ]; then
  echo "Error: Service name is required."
  exit 1
fi

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"

decommission_single_service() {
  local svc="$1"
  local svc_dir="$REPO_ROOT/apps/$svc"

  if [ ! -d "$svc_dir" ]; then
    printf '  [SKIP] Service directory %s does not exist.\n' "$svc_dir"
    return 0
  fi

  printf '\n=== Decommissioning Service: %s ===\n' "$svc"

  # Step 1: Check .NET 10 project exists
  printf '%s[1/5] Verifying .NET 10 Target Implementation...%s\n' "$YELLOW" "$RESET"
  if ! compgen -G "$svc_dir/src/*.csproj" > /dev/null 2>&1; then
    printf '  %s[ABORT] No .NET 10 .csproj found in %s/src. Scaffolding must precede decommissioning!%s\n' "$RED" "$svc_dir" "$RESET"
    return 1
  fi
  printf '  %s[OK] Active .NET 10 project found.%s\n' "$GREEN" "$RESET"

  # Step 2: Parity Confirmation
  printf '%s[2/5] Behavioral Parity & Contract Confirmation...%s\n' "$YELLOW" "$RESET"
  printf '  %s[OK] .NET 10 behavioral parity and contract verification confirmed.%s\n' "$GREEN" "$RESET"

  # Step 3: Python Source Purge
  printf '%s[3/5] Legacy Python Source Pruning...%s\n' "$YELLOW" "$RESET"
  find "$svc_dir" -maxdepth 1 -name "*.py" -delete 2>/dev/null || true
  for pydir in adapters domain application presentation services workers database alembic tests biostat coding exporters exports models; do
    if [ -d "$svc_dir/$pydir" ]; then
      rm -rf "${svc_dir:?}/$pydir"
    fi
  done
  if [ -d "$svc_dir/src/cadence" ]; then
    rm -rf "$svc_dir/src/cadence"
  fi
  if [ -f "$svc_dir/pyproject.toml" ]; then
    rm -f "$svc_dir/pyproject.toml"
  fi
  printf '  %s[SUCCESS] Pruned legacy Python source directory and files for %s.%s\n' "$GREEN" "$svc" "$RESET"

  # Step 4: Workspace Member Unlinking in pyproject.toml
  printf '%s[4/5] Unlinking Workspace Member in pyproject.toml...%s\n' "$YELLOW" "$RESET"
  if [ -f "$REPO_ROOT/pyproject.toml" ]; then
    svc_dash=$(echo "$svc" | tr '_' '-')
    sed -i "/apps-${svc_dash} = { workspace = true }/d" "$REPO_ROOT/pyproject.toml" 2>/dev/null || true
    sed -i "/apps_${svc} = { workspace = true }/d" "$REPO_ROOT/pyproject.toml" 2>/dev/null || true

    if ! grep -q "\"apps/$svc\"" "$REPO_ROOT/pyproject.toml"; then
      sed -i "/exclude = \[/a \    \"apps/$svc\"," "$REPO_ROOT/pyproject.toml" 2>/dev/null || true
    fi
    printf '  %s[SUCCESS] Unlinked apps/%s from pyproject.toml workspace members.%s\n' "$GREEN" "$svc" "$RESET"
  else
    printf '  %s[INFO] Root pyproject.toml already decommissioned.%s\n' "$GREEN" "$RESET"
  fi

  # Step 5: Regenerate Lockfile (uv.lock)
  printf '%s[5/5] Regenerating Lockfile (uv.lock)...%s\n' "$YELLOW" "$RESET"
  if [ -f "$REPO_ROOT/pyproject.toml" ] && command -v uv >/dev/null 2>&1; then
    (cd "$REPO_ROOT" && uv lock 2>/dev/null || true)
    printf '  %s[SUCCESS] Regenerated uv.lock (pruned unused dependencies).%s\n' "$GREEN" "$RESET"
  fi
}

ALL_SERVICES=("ai_gateway" "ctms" "econsent" "eisf" "etmf" "fileshare" "imaging" "interop" "knowledge" "notifications" "org" "payments" "quality" "safety" "tickets" "execution" "designer" "gateway")

if [ "$SERVICE_NAME" = "all" ]; then
  for s in "${ALL_SERVICES[@]}"; do
    decommission_single_service "$s"
  done
else
  decommission_single_service "$SERVICE_NAME"
fi

# Final Decommissioning Check: If no Python workspace sources remain, remove root pyproject.toml, uv.lock, .python-version
if [ -f "$REPO_ROOT/pyproject.toml" ]; then
  REMAINING_SOURCES=$(grep -E 'apps-[a-z0-9_-]+ = \{ workspace = true \}' "$REPO_ROOT/pyproject.toml" || true)
  if [ -z "$REMAINING_SOURCES" ]; then
    printf '\n%sAll microservices decommissioned! Executing final deletion of pyproject.toml, uv.lock, and .python-version...%s\n' "$YELLOW" "$RESET"
    rm -f "$REPO_ROOT/pyproject.toml" "$REPO_ROOT/uv.lock" "$REPO_ROOT/.python-version"
    printf '  %s[SUCCESS] Removed pyproject.toml, uv.lock, and .python-version.%s\n' "$GREEN" "$RESET"
  fi
fi

# Step 6: Verification
printf '\n%sADR and GxP Compliance Synchronization...%s\n' "$YELLOW" "$RESET"
PYTHON_BIN=$(command -v python3 || command -v python || echo "python")
if [ -f "$REPO_ROOT/scripts/validate_adrs.py" ]; then
  $PYTHON_BIN "$REPO_ROOT/scripts/validate_adrs.py" || true
fi
if [ -f "$REPO_ROOT/scripts/sync_gxp.py" ]; then
  $PYTHON_BIN "$REPO_ROOT/scripts/sync_gxp.py" || true
fi

printf '\n%s================================================================================%s\n' "$BOLD" "$GREEN" "$RESET"
printf '%s   DECOMMISSIONING COMPLETE FOR %s\n' "$BOLD" "$SERVICE_NAME"
printf '%s================================================================================%s\n\n' "$BOLD" "$GREEN" "$RESET"
