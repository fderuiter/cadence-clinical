#!/usr/bin/env bash
# ==============================================================================
# Cadence Clinical Platform — Full-Stack Developer Environment Setup Wizard
# Requirements: PRD-SYS-053, ADR-2233, ADR-2527
#
# Thin fail-closed bootstrap wrapper around `scripts/setup_dotnet_env.sh` and
# `node scripts/run-cli.js doctor --auto-fix`.
#
# Usage: scripts/setup_wizard.sh [--dev|--non-interactive|-y] [--minimal] [--linters-only] [--help]
# ==============================================================================

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

AUTO_ACCEPT="${CADENCE_WIZARD_AUTO_ACCEPT:-0}"
MINIMAL=0
LINTERS_ONLY=0

for arg in "$@"; do
  case "$arg" in
    --dev|--non-interactive|-y)
      AUTO_ACCEPT=1
      ;;
    --minimal)
      MINIMAL=1
      ;;
    --linters-only)
      LINTERS_ONLY=1
      AUTO_ACCEPT=1
      ;;
    -h|--help)
      printf "Usage: %s [--dev] [--minimal] [--linters-only] [--help]\n" "$0"
      printf "  --dev, --non-interactive, -y  Skip prompts and accept all defaults\n"
      printf "  --minimal                     Skip heavy/optional steps (Playwright browsers, cert trust)\n"
      printf "  --linters-only                Provision only SHA-256 verified actionlint, hadolint, and shellcheck in .cadence/bin\n"
      exit 0
      ;;
    *)
      printf "Unknown option: %s\n" "$arg" >&2
      exit 1
      ;;
  esac
done

if [[ ! -t 0 ]] || [[ "${CI:-}" == "true" ]] || [[ "${CLAUDE_CODE_REMOTE:-}" == "true" ]]; then
  AUTO_ACCEPT=1
fi

if [[ -t 1 ]] && command -v tput >/dev/null 2>&1 && [[ "$(tput colors 2>/dev/null || echo 0)" -ge 8 ]]; then
  BOLD=$(tput bold); DIM=$(tput dim); RESET=$(tput sgr0)
  GREEN=$(tput setaf 2); CYAN=$(tput setaf 6); RED=$(tput setaf 1)
else
  BOLD=""; DIM=""; RESET=""; GREEN=""; CYAN=""; RED=""
fi

fail_closed() {
  printf '  %s[ERROR] %s%s\n' "$RED" "$1" "$RESET" >&2
  exit 1
}

if [[ "$LINTERS_ONLY" != "1" ]]; then
  printf '\n%s%s================================================================================%s\n' "$BOLD" "$CYAN" "$RESET"
  printf '%s%s   CADENCE CLINICAL PLATFORM — FULL-STACK DEVELOPER SETUP WIZARD                 %s\n' "$BOLD" "$CYAN" "$RESET"
  printf '%s%s================================================================================%s\n' "$BOLD" "$CYAN" "$RESET"

  OS_NAME="$(uname -s)"
  CONTEXT="local workstation"
  if [[ "${CLAUDE_CODE_REMOTE:-}" == "true" ]]; then
    CONTEXT="Claude Code remote session"
  elif [[ -n "${CODESPACES:-}" ]]; then
    CONTEXT="GitHub Codespaces"
  elif [[ -n "${REMOTE_CONTAINERS:-}" ]] || [[ -n "${DEVCONTAINER:-}" ]]; then
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

  DOTNET_ENV_ARGS=()
  if [[ "$MINIMAL" == "1" ]]; then
    DOTNET_ENV_ARGS+=("--skip-cert-trust")
  fi
  if ! bash "$REPO_ROOT/scripts/setup_dotnet_env.sh" "${DOTNET_ENV_ARGS[@]}"; then
    fail_closed ".NET 10 SDK setup failed — aborting before configuring Git hooks."
  fi
fi

if [[ -f "$REPO_ROOT/scripts/resolve_dotnet_env.sh" ]]; then
  export CADENCE_RESOLVE_REPO_ROOT="$REPO_ROOT"
  # shellcheck source=scripts/resolve_dotnet_env.sh disable=SC1091
  . "$REPO_ROOT/scripts/resolve_dotnet_env.sh"
  unset CADENCE_RESOLVE_REPO_ROOT
fi

if ! command -v dotnet >/dev/null 2>&1 || ! dotnet --version >/dev/null 2>&1; then
  fail_closed ".NET 10 SDK (dotnet) not found or unusable on PATH."
fi

if ! command -v node >/dev/null 2>&1 || ! node --version >/dev/null 2>&1; then
  fail_closed "Node.js (node) not found on PATH. Install Node.js 22.13+ or 24+ and re-run setup_wizard.sh."
fi

if command -v corepack >/dev/null 2>&1; then
  corepack enable >/dev/null 2>&1 || true
fi

if ! command -v pnpm >/dev/null 2>&1 || ! pnpm --version >/dev/null 2>&1; then
  fail_closed "pnpm not found or crashes on PATH. Install Corepack ('npm install --global corepack') and run 'corepack enable'."
fi

DOCTOR_ARGS=("doctor" "--auto-fix")
if [[ "$MINIMAL" == "1" ]]; then
  DOCTOR_ARGS+=("--minimal")
fi
if [[ "$LINTERS_ONLY" == "1" ]]; then
  DOCTOR_ARGS+=("--linters-only")
fi

if ! node "$REPO_ROOT/scripts/run-cli.js" "${DOCTOR_ARGS[@]}"; then
  fail_closed "'cadence doctor --auto-fix' failed — review the errors above."
fi

if [[ "$LINTERS_ONLY" != "1" ]]; then
  printf '\n%s%s================================================================================%s\n' "$BOLD" "$GREEN" "$RESET"
  printf '%s%s   SETUP COMPLETE! Run '"'"'make verify'"'"' to confirm the full stack passes.        %s\n' "$BOLD" "$GREEN" "$RESET"
  printf '%s%s================================================================================%s\n\n' "$BOLD" "$GREEN" "$RESET"
fi
