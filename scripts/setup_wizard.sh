#!/usr/bin/env bash
# ==============================================================================
# Cadence Clinical Platform — Full-Stack Developer Environment Setup Wizard
# Requirements: PRD-SYS-053, ADR-2233
#
# Provisions everything a fresh checkout needs across .NET, Node, and Python —
# on a local workstation, GitHub Codespaces, a VS Code devcontainer, a Claude
# Code (or other agent) remote session, or CI. Safe to re-run any time.
#
# Usage: scripts/setup_wizard.sh [--dev|--non-interactive|-y] [--minimal] [--help]
#   --dev, --non-interactive, -y  Skip prompts and accept all defaults
#   --minimal                     Skip heavy/optional steps (Playwright browser
#                                  download, HTTPS dev-cert trust prompts) —
#                                  good for short-lived remote/agent sessions
# ==============================================================================

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

TOTAL_STAGES=6
_STAGE_INDEX=0
AUTO_ACCEPT="${CADENCE_WIZARD_AUTO_ACCEPT:-0}"
MINIMAL=0

for arg in "$@"; do
  case "$arg" in
    --dev|--non-interactive|-y)
      AUTO_ACCEPT=1
      ;;
    --minimal)
      MINIMAL=1
      ;;
    -h|--help)
      printf "Usage: %s [--dev] [--minimal] [--help]\n" "$0"
      printf "  --dev, --non-interactive, -y  Skip prompts and accept all defaults\n"
      printf "  --minimal                     Skip heavy/optional steps (Playwright browsers, cert trust)\n"
      exit 0
      ;;
  esac
done

# Auto-detect non-interactive contexts so CI / remote agent sessions never hang on a prompt.
if [[ ! -t 0 ]] || [[ "${CI:-}" == "true" ]] || [[ "${CLAUDE_CODE_REMOTE:-}" == "true" ]]; then
  AUTO_ACCEPT=1
fi

# Formatting
if [[ -t 1 ]] && command -v tput >/dev/null 2>&1 && [[ "$(tput colors 2>/dev/null || echo 0)" -ge 8 ]]; then
  BOLD=$(tput bold); DIM=$(tput dim); RESET=$(tput sgr0)
  GREEN=$(tput setaf 2); YELLOW=$(tput setaf 3); CYAN=$(tput setaf 6)
else
  BOLD=""; DIM=""; RESET=""; GREEN=""; YELLOW=""; CYAN=""
fi

ok()   { printf '  %s[OK] %s%s\n' "$GREEN" "$1" "$RESET"; }
info() { printf '  %s[INFO] %s%s\n' "$YELLOW" "$1" "$RESET"; }

FAILED_STAGES=()
warn_failed() { FAILED_STAGES+=("$1"); info "$1"; }

stage() {
  _STAGE_INDEX=$(( _STAGE_INDEX + 1 ))
  printf '\n%s%s[%d/%d] %s%s\n' "$BOLD" "$CYAN" "$_STAGE_INDEX" "$TOTAL_STAGES" "$1" "$RESET"
  printf '%s  %s%s\n' "$DIM" "$2" "$RESET"
}

printf '\n%s%s================================================================================%s\n' "$BOLD" "$CYAN" "$RESET"
printf '%s%s   CADENCE CLINICAL PLATFORM — FULL-STACK DEVELOPER SETUP WIZARD                 %s\n' "$BOLD" "$CYAN" "$RESET"
printf '%s%s================================================================================%s\n' "$BOLD" "$CYAN" "$RESET"

# --------------------------------------------------------------------------
# Stage 0: Environment detection
# --------------------------------------------------------------------------
OS_NAME="$(uname -s)"
CONTEXT="local workstation"
if [[ "${CLAUDE_CODE_REMOTE:-}" == "true" ]]; then
  CONTEXT="Claude Code remote session"
elif [[ -n "${CODESPACES:-}" ]]; then
  CONTEXT="GitHub Codespaces"
elif [[ -n "${REMOTE_CONTAINERS:-}" || -n "${DEVCONTAINER:-}" ]]; then
  CONTEXT="VS Code devcontainer"
elif [[ "${CI:-}" == "true" ]]; then
  CONTEXT="CI runner"
fi
printf '%s  Detected environment: %s (%s)%s\n' "$DIM" "$CONTEXT" "$OS_NAME" "$RESET"
printf '%s  Mode: %s%s\n\n' "$DIM" "$([[ "$AUTO_ACCEPT" == "1" ]] && echo "non-interactive" || echo "interactive")" "$RESET"

if [[ "$AUTO_ACCEPT" != "1" ]]; then
  read -r -p "Ready to begin full-stack setup? [Y/n]: " START_PROMPT || true
  if [[ "$START_PROMPT" =~ ^[nN] ]]; then
    echo "Setup paused. Re-run anytime with: scripts/setup_wizard.sh"
    exit 0
  fi
fi

# --------------------------------------------------------------------------
# Stage 1: .NET 10 SDK
# --------------------------------------------------------------------------
stage ".NET 10 SDK" "Installing/validating the .NET 10 SDK, global tools, and dev certs..."
bash "$REPO_ROOT/scripts/setup_dotnet_env.sh" || warn_failed ".NET SDK setup step failed — see output above. Continuing with remaining stages."
if [[ "$MINIMAL" == "1" ]]; then
  info "Skipped HTTPS dev-cert trust prompt handling (--minimal)."
fi
# Warm the NuGet cache for a faster first build. Some projects currently fail
# to restore due to pinned preview package versions in Directory.Packages.props
# (a pre-existing repo issue, not caused by this wizard) — don't fail on it.
if command -v dotnet >/dev/null 2>&1; then
  dotnet restore "$REPO_ROOT/Cadence.sln" || info "Some projects failed to restore (see Directory.Packages.props pins)."
fi

# --------------------------------------------------------------------------
# Stage 2: Node.js / pnpm
# --------------------------------------------------------------------------
stage "Node.js & pnpm" "Enabling corepack and installing workspace dependencies..."
if ! command -v node >/dev/null 2>&1; then
  info "Node.js not found on PATH. Install it (e.g. via nvm or your OS package manager), then re-run this wizard."
else
  ok "Node.js $(node --version) detected."
  if command -v corepack >/dev/null 2>&1; then
    corepack enable >/dev/null 2>&1 || true
  fi
  if command -v pnpm >/dev/null 2>&1; then
    if pnpm install --frozen-lockfile; then
      ok "pnpm workspace dependencies installed."
    else
      warn_failed "pnpm install failed — see output above. Continuing with remaining stages."
    fi
  else
    info "pnpm not found even after 'corepack enable'; install it manually (npm i -g pnpm)."
  fi
fi

# --------------------------------------------------------------------------
# Stage 3: Python / uv
# --------------------------------------------------------------------------
stage "Python & uv" "Syncing the Python toolchain, pre-commit hooks, and (optionally) Playwright..."
if ! command -v uv >/dev/null 2>&1; then
  info "uv not found on PATH. Install it: curl -LsSf https://astral.sh/uv/install.sh | sh"
else
  if uv sync --python 3.14 --all-extras; then
    ok "uv sync complete."
  else
    warn_failed "uv sync failed — see output above. Continuing with remaining stages."
  fi

  # A cached uv-managed Python 3.14 pre-release (e.g. a release candidate
  # downloaded before 3.14 went final) crashes every pydantic model import
  # (typing._eval_type() incompatibility) despite pyproject.toml pinning
  # python-preference=system, because that preference only helps once a real
  # system Python 3.14 exists. Verify what got resolved and self-heal.
  PY_RELEASELEVEL="$(uv run --python 3.14 python -c 'import sys; print(sys.version_info.releaselevel)' 2>/dev/null || echo unknown)"
  if [[ "$PY_RELEASELEVEL" != "final" ]]; then
    info "Resolved Python 3.14 interpreter is not a final release (releaselevel=$PY_RELEASELEVEL) — it will crash on pydantic model import. Installing a system Python 3.14 and evicting the stale one..."
    SUDO=""
    if [ "$(id -u)" -ne 0 ]; then SUDO="sudo"; fi
    if command -v apt-get >/dev/null 2>&1 && [ -f /etc/debian_version ]; then
      $SUDO apt-get update -qq && $SUDO apt-get install -y --no-install-recommends python3.14 python3.14-venv || true
    fi
    BAD_VERSION="$(uv run --python 3.14 python -c 'import platform; print(platform.python_version())' 2>/dev/null || true)"
    [[ -n "$BAD_VERSION" ]] && uv python uninstall "$BAD_VERSION" >/dev/null 2>&1
    rm -rf "$REPO_ROOT/.venv"
    if uv sync --python 3.14 --all-extras \
       && [[ "$(uv run --python 3.14 python -c 'import sys; print(sys.version_info.releaselevel)' 2>/dev/null)" == "final" ]]; then
      ok "Recovered: uv now resolves a final-release Python 3.14 interpreter."
    else
      warn_failed "Could not obtain a final-release Python 3.14 interpreter — pydantic model imports (packages/deid included) will crash. Install python3.14 system-wide (e.g. via the deadsnakes PPA) or run 'uv python uninstall $BAD_VERSION' manually, then re-run this wizard."
    fi
  fi

  uv run --python 3.14 pre-commit install --install-hooks \
    && ok "pre-commit hooks installed." \
    || warn_failed "pre-commit hook install failed — see output above."
  if [[ "$MINIMAL" == "1" ]]; then
    info "Skipped Playwright browser download (--minimal). Run 'uv run playwright install chromium' later if needed."
  else
    uv run --python 3.14 playwright install chromium \
      && ok "Playwright Chromium installed." \
      || warn_failed "Playwright browser install failed — see output above."
  fi
fi

# --------------------------------------------------------------------------
# Stage 4: Local datastores
# --------------------------------------------------------------------------
stage "Clinical Datastores" "Probing PostgreSQL (5432) and Neo4j (7687)..."
if command -v nc >/dev/null 2>&1 && nc -z 127.0.0.1 5432 2>/dev/null; then
  ok "PostgreSQL running on port 5432."
else
  info "PostgreSQL not reachable. Start via: docker compose up -d postgres"
fi
if command -v nc >/dev/null 2>&1 && nc -z 127.0.0.1 7687 2>/dev/null; then
  ok "Neo4j Bolt running on port 7687."
else
  info "Neo4j not reachable. Start via: docker compose up -d neo4j"
fi

# --------------------------------------------------------------------------
# Stage 5: Environment configuration (.env)
# --------------------------------------------------------------------------
stage "Environment Configuration (.env)" "Ensuring local .env is initialized..."
if [[ ! -f "$REPO_ROOT/.env" ]] && [[ -f "$REPO_ROOT/.env.example" ]]; then
  cp "$REPO_ROOT/.env.example" "$REPO_ROOT/.env"
  ok "Initialized .env from template. Fill in secrets with: pnpm cadence setup --dev"
else
  ok ".env file is present."
fi

# --------------------------------------------------------------------------
# Stage 6: Final validation
# --------------------------------------------------------------------------
stage "Final Validation" "Running 'cadence doctor' across the whole stack..."
if command -v uv >/dev/null 2>&1; then
  uv run --python 3.14 cadence doctor || warn_failed "'cadence doctor' reported issues above — review before continuing."
else
  info "Skipped 'cadence doctor' (uv not available)."
fi

if [[ "${#FAILED_STAGES[@]}" -eq 0 ]]; then
  printf '\n%s%s================================================================================%s\n' "$BOLD" "$GREEN" "$RESET"
  printf '%s%s   SETUP COMPLETE! Run '"'"'make verify'"'"' to confirm the full stack passes.        %s\n' "$BOLD" "$GREEN" "$RESET"
  printf '%s%s================================================================================%s\n\n' "$BOLD" "$GREEN" "$RESET"
else
  printf '\n%s%s================================================================================%s\n' "$BOLD" "$YELLOW" "$RESET"
  printf '%s%s   SETUP FINISHED WITH %d ISSUE(S) — review the [INFO] lines above:%s\n' "$BOLD" "$YELLOW" "${#FAILED_STAGES[@]}" "$RESET"
  for msg in "${FAILED_STAGES[@]}"; do
    printf '%s     - %s%s\n' "$YELLOW" "$msg" "$RESET"
  done
  printf '%s%s================================================================================%s\n\n' "$BOLD" "$YELLOW" "$RESET"
fi
