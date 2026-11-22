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
      printf "  --linters-only                Provision only SHA-256 verified actionlint and hadolint in .cadence/bin\n"
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

compute_sha256() {
  local target_file="$1"
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$target_file" | awk '{print $1}'
  elif command -v shasum >/dev/null 2>&1; then
    shasum -a 256 "$target_file" | awk '{print $1}'
  else
    return 1
  fi
}

verify_sha256() {
  local artifact_label="$1"
  local target_file="$2"
  local expected_hash="$3"
  local actual_hash=""
  if ! actual_hash="$(compute_sha256 "$target_file")"; then
    fail_blocking "Neither sha256sum nor shasum is available to verify SHA-256 checksum for ${artifact_label}."
    return 1
  fi

  local expected_lower
  local actual_lower
  expected_lower="$(printf '%s' "$expected_hash" | tr '[:upper:]' '[:lower:]')"
  actual_lower="$(printf '%s' "$actual_hash" | tr '[:upper:]' '[:lower:]')"
  if [[ "$actual_lower" != "$expected_lower" ]]; then
    fail_blocking "Checksum verification failed for ${artifact_label} (expected ${expected_lower}, got ${actual_lower})."
    return 1
  fi

  return 0
}

provision_cli_linters() {
  local cadence_bin="${CADENCE_BIN_DIR:-$REPO_ROOT/.cadence/bin}"
  mkdir -p "$cadence_bin"

  local os_lower arch_raw actionlint_arch hadolint_os hadolint_arch
  os_lower="$(uname -s | tr '[:upper:]' '[:lower:]')"
  arch_raw="$(uname -m)"

  case "$arch_raw" in
    x86_64|amd64)
      actionlint_arch="amd64"
      hadolint_arch="x86_64"
      ;;
    aarch64|arm64)
      actionlint_arch="arm64"
      hadolint_arch="arm64"
      ;;
    *)
      fail_blocking "Unsupported CPU architecture '${arch_raw}' for actionlint/hadolint provisioning."
      return 1
      ;;
  esac

  local pinned_actionlint_sha=""
  local pinned_hadolint_sha=""
  case "${os_lower}_${actionlint_arch}" in
    linux_amd64)
      hadolint_os="linux"
      pinned_actionlint_sha="8aca8db96f1b94770f1b0d72b6dddcb1ebb8123cb3712530b08cc387b349a3d8" # pragma: allowlist secret
      pinned_hadolint_sha="c7187db94eeeeca956519a6af171adc31453941a1e777961f6e680f697c8c507" # pragma: allowlist secret
      ;;
    linux_arm64)
      hadolint_os="linux"
      pinned_actionlint_sha="325e971b6ba9bfa504672e29be93c24981eeb1c07576d730e9f7c8805afff0c6" # pragma: allowlist secret
      pinned_hadolint_sha="f6198ef8090f404dbb771abfee086eb8c48ac177f30da7fd3510aca35b344b5d" # pragma: allowlist secret
      ;;
    darwin_amd64)
      hadolint_os="macos"
      pinned_actionlint_sha="5b44c3bc2255115c9b69e30efc0fecdf498fdb63c5d58e17084fd5f16324c644" # pragma: allowlist secret
      pinned_hadolint_sha="ffe9bb18b23d5ed1eae50237aecdbb523d016e96da0bd4e7aa432040acfc3fde" # pragma: allowlist secret
      ;;
    darwin_arm64)
      hadolint_os="macos"
      pinned_actionlint_sha="aba9ced2dee8d27fecca3dc7feb1a7f9a52caefa1eb46f3271ea66b6e0e6953f" # pragma: allowlist secret
      pinned_hadolint_sha="5c09f3213f8e40406abe048233d985eebef336d4a6a20021be47fadb6cf480a2" # pragma: allowlist secret
      ;;
    *)
      fail_blocking "Unsupported OS '${os_lower}' for actionlint/hadolint provisioning."
      return 1
      ;;
  esac

  local actionlint_version="1.7.12"
  local hadolint_version="2.15.1"
  local actionlint_bin="$cadence_bin/actionlint"
  local hadolint_bin="$cadence_bin/hadolint"

  if [[ -x "$actionlint_bin" ]] && "$actionlint_bin" -version 2>/dev/null | grep -Fq "$actionlint_version" \
     && [[ -z "${ACTIONLINT_SHA256:-}" ]] && [[ -z "${ACTIONLINT_DOWNLOAD_URL:-}" ]]; then
    ok "actionlint ${actionlint_version} already provisioned in .cadence/bin."
  else
    local actionlint_asset="actionlint_${actionlint_version}_${os_lower}_${actionlint_arch}.tar.gz"
    local actionlint_url="${ACTIONLINT_DOWNLOAD_URL:-https://github.com/rhysd/actionlint/releases/download/v${actionlint_version}/${actionlint_asset}}"
    local expected_actionlint_sha="${ACTIONLINT_SHA256:-$pinned_actionlint_sha}"
    local tmp_actionlint_dir
    tmp_actionlint_dir="$(mktemp -d /tmp/cadence-actionlint.XXXXXX)"
    local tmp_actionlint_archive="$tmp_actionlint_dir/$actionlint_asset"

    if ! curl -fsSL --connect-timeout 15 --retry 3 "$actionlint_url" -o "$tmp_actionlint_archive"; then
      rm -rf "$tmp_actionlint_dir"
      fail_blocking "Failed to download actionlint v${actionlint_version} from ${actionlint_url}."
    elif ! verify_sha256 "actionlint (${actionlint_asset})" "$tmp_actionlint_archive" "$expected_actionlint_sha"; then
      rm -rf "$tmp_actionlint_dir"
    elif ! tar -xzf "$tmp_actionlint_archive" -C "$tmp_actionlint_dir" actionlint || [[ ! -f "$tmp_actionlint_dir/actionlint" ]]; then
      rm -rf "$tmp_actionlint_dir"
      fail_blocking "Failed to extract actionlint binary from ${actionlint_asset}."
    else
      mv "$tmp_actionlint_dir/actionlint" "$actionlint_bin"
      chmod +x "$actionlint_bin"
      rm -rf "$tmp_actionlint_dir"
      ok "actionlint ${actionlint_version} SHA-256 verified and installed in .cadence/bin."
    fi
  fi

  if [[ -x "$hadolint_bin" ]] && "$hadolint_bin" --version 2>/dev/null | grep -Fq "$hadolint_version" \
     && [[ -z "${HADOLINT_SHA256:-}" ]] && [[ -z "${HADOLINT_DOWNLOAD_URL:-}" ]]; then
    ok "hadolint ${hadolint_version} already provisioned in .cadence/bin."
  else
    local hadolint_asset="hadolint-${hadolint_os}-${hadolint_arch}"
    local hadolint_url="${HADOLINT_DOWNLOAD_URL:-https://github.com/hadolint/hadolint/releases/download/v${hadolint_version}/${hadolint_asset}}"
    local expected_hadolint_sha="${HADOLINT_SHA256:-$pinned_hadolint_sha}"
    local tmp_hadolint
    tmp_hadolint="$(mktemp /tmp/cadence-hadolint.XXXXXX)"

    if ! curl -fsSL --connect-timeout 15 --retry 3 "$hadolint_url" -o "$tmp_hadolint"; then
      rm -f "$tmp_hadolint"
      fail_blocking "Failed to download hadolint v${hadolint_version} from ${hadolint_url}."
    elif ! verify_sha256 "hadolint (${hadolint_asset})" "$tmp_hadolint" "$expected_hadolint_sha"; then
      rm -f "$tmp_hadolint"
    else
      mv "$tmp_hadolint" "$hadolint_bin"
      chmod +x "$hadolint_bin"
      ok "hadolint ${hadolint_version} SHA-256 verified and installed in .cadence/bin."
    fi
  fi
}

if [[ "$LINTERS_ONLY" == "1" ]]; then
  provision_cli_linters
  exit "$BLOCKING_FAILURE"
fi

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
CADENCE_RESOLVE_REPO_ROOT="$REPO_ROOT"
# shellcheck source=scripts/resolve_dotnet_env.sh
. "$REPO_ROOT/scripts/resolve_dotnet_env.sh"
unset CADENCE_RESOLVE_REPO_ROOT
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

provision_cli_linters

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
