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
  GREEN=$(tput setaf 2); YELLOW=$(tput setaf 3); CYAN=$(tput setaf 6); RED=$(tput setaf 1)
else
  BOLD=""; DIM=""; RESET=""; GREEN=""; YELLOW=""; CYAN=""; RED=""
fi

ok()   { printf '  %s[OK] %s%s\n' "$GREEN" "$1" "$RESET"; }
info() { printf '  %s[INFO] %s%s\n' "$YELLOW" "$1" "$RESET"; }

FAILED_STAGES=()
BLOCKING_FAILURE=0
warn_failed() { FAILED_STAGES+=("$1"); info "$1"; }
# A failure that breaks every later command (for example pnpm): reported in red and makes the wizard exit non-zero.
fail_blocking() { FAILED_STAGES+=("$1"); BLOCKING_FAILURE=1; printf '  %s[ERROR] %s%s\n' "$RED" "$1" "$RESET" >&2; }

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
    # package.json pins pnpm with packageManager (#5331); corepack runs exactly that version.
    if ! PNPM_VERSION=$(pnpm --version 2>/dev/null); then
      fail_blocking "pnpm is on the PATH but crashes. Run 'corepack enable' so pnpm follows the packageManager pin in package.json."
    elif pnpm install --frozen-lockfile; then
      ok "pnpm ${PNPM_VERSION}: workspace dependencies installed."
    else
      fail_blocking "pnpm install failed — see output above. Every 'pnpm cadence' command needs it."
    fi
  else
    fail_blocking "pnpm not found. Install Corepack if needed ('npm install --global corepack'), then run 'corepack enable'."
  fi
fi

# --------------------------------------------------------------------------
# Stage 3: Git Pre-Commit Hooks & Quality Gates
# --------------------------------------------------------------------------
stage "Git Quality Gates" "Configuring native .NET pre-commit hooks and secret-scanning dependencies..."
if git config core.hooksPath .githooks && chmod +x "$REPO_ROOT/.githooks/pre-commit" 2>/dev/null; then
  ok "Native git pre-commit quality hooks enabled (.githooks/pre-commit -> cadence check --pre-commit)."
else
  warn_failed "Failed to configure .githooks/pre-commit. Run 'git config core.hooksPath .githooks' manually."
fi

CADENCE_VENV="$REPO_ROOT/.cadence/venv"
if [[ -x "$CADENCE_VENV/bin/detect-secrets-hook" ]] && "$CADENCE_VENV/bin/detect-secrets-hook" --version >/dev/null 2>&1 \
   && [[ -x "$CADENCE_VENV/bin/pip-audit" ]] && "$CADENCE_VENV/bin/pip-audit" --version >/dev/null 2>&1; then
  ok "detect-secrets $("$CADENCE_VENV/bin/detect-secrets-hook" --version) and pip-audit already provisioned in .cadence/venv."
elif command -v uv >/dev/null 2>&1; then
  if uv venv "$CADENCE_VENV" >/dev/null 2>&1 && uv pip install --python "$CADENCE_VENV/bin/python" --quiet detect-secrets==1.5.0 pip-audit==2.10.1; then
    ok "detect-secrets 1.5.0 and pip-audit 2.10.1 provisioned in .cadence/venv via uv."
  else
    fail_blocking "Failed to provision detect-secrets==1.5.0 and pip-audit==2.10.1 in .cadence/venv via uv."
  fi
elif command -v python3 >/dev/null 2>&1; then
  if python3 -m venv "$CADENCE_VENV" && "$CADENCE_VENV/bin/python" -m pip install --quiet --upgrade pip detect-secrets==1.5.0 pip-audit==2.10.1; then
    ok "detect-secrets 1.5.0 and pip-audit 2.10.1 provisioned in .cadence/venv via python3 -m venv."
  else
    fail_blocking "Failed to provision detect-secrets==1.5.0 and pip-audit==2.10.1 in .cadence/venv. Ensure python3-venv is installed."
  fi
else
  fail_blocking "Neither 'uv' nor 'python3' was found on PATH to provision detect-secrets==1.5.0 and pip-audit==2.10.1 for pre-commit guardrails."
fi

if [[ "$MINIMAL" == "1" ]]; then
  info "Skipped Playwright browser download (--minimal)."
else
  if command -v pnpm >/dev/null 2>&1; then
    if pnpm --filter web exec playwright install chromium 2>/dev/null; then
      ok "Playwright Chromium browser installed for web E2E testing."
    else
      info "Playwright browser install skipped (can be installed later via 'pnpm --filter web exec playwright install')."
    fi
  fi
fi

# --------------------------------------------------------------------------
# Stage 4: Local datastores
# --------------------------------------------------------------------------
stage "Clinical Datastores" "Probing PostgreSQL (5432)..."
if command -v nc >/dev/null 2>&1 && nc -z 127.0.0.1 5432 2>/dev/null; then
  ok "PostgreSQL running on port 5432."
else
  info "PostgreSQL not reachable. Start via: docker compose -f docker/docker-compose.yml up -d postgres"
fi

# --------------------------------------------------------------------------
# Stage 5: Environment configuration (.env)
# --------------------------------------------------------------------------
stage "Environment Configuration (.env)" "Ensuring local .env is initialized..."
if [[ ! -f "$REPO_ROOT/.env" ]] && [[ -f "$REPO_ROOT/.env.example" ]]; then
  cp "$REPO_ROOT/.env.example" "$REPO_ROOT/.env"
  ok "Initialized .env from template. Configure secrets for local development."
else
  ok ".env file is present."
fi

# --------------------------------------------------------------------------
# Stage 6: Final validation
# --------------------------------------------------------------------------
stage "Final Validation" "Running 'cadence doctor' across the whole stack..."
if command -v dotnet >/dev/null 2>&1; then
  node scripts/run-cli.js doctor || warn_failed "'cadence doctor' reported issues above — review before continuing."
else
  info "Skipped 'cadence doctor' (.NET SDK not available)."
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

exit "$BLOCKING_FAILURE"
