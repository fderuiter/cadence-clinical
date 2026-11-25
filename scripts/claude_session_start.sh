#!/bin/bash
# SessionStart hook: runs the repo's full-stack setup wizard (non-interactive,
# --minimal) so `dotnet`, `pnpm` and Cadence CLI commands all work in
# Claude Code on the web sessions. Only runs remotely.
set -euo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

cd "$CLAUDE_PROJECT_DIR"

DOTNET_TOOLS_DIR="${HOME}/.dotnet/tools"
mkdir -p "$DOTNET_TOOLS_DIR"
export PATH="$PATH:$DOTNET_TOOLS_DIR"
if [ -n "${CLAUDE_ENV_FILE:-}" ] && ! grep -qs '.dotnet/tools' "$CLAUDE_ENV_FILE" 2>/dev/null; then
  echo "export PATH=\"\$PATH:$DOTNET_TOOLS_DIR\"" >> "$CLAUDE_ENV_FILE"
fi

# The cloud environment's setup script creates a PostgreSQL 18 test cluster (UTF8, trust auth) on 5432
# (docs/TESTING.md); a resumed container does not restart it. `cadence ci` also runs the browser check.
if command -v pg_lsclusters >/dev/null 2>&1 && pg_lsclusters -h 2>/dev/null | grep -q '^18 main'; then
  pg_ctlcluster 18 main start 2>/dev/null || true
fi

bash scripts/setup_wizard.sh --dev --minimal

playwright_chromium_ready() {
  if [ -n "${PLAYWRIGHT_CHROMIUM_PATH:-}" ] && [ -x "${PLAYWRIGHT_CHROMIUM_PATH}" ]; then
    return 0
  fi
  if [ -x "/opt/pw-browsers/chromium" ]; then
    return 0
  fi
  pnpm --filter web exec node --input-type=module -e '
    import fs from "node:fs";
    import { chromium } from "@playwright/test";
    const executablePath = chromium.executablePath();
    process.exit(executablePath && fs.existsSync(executablePath) ? 0 : 1);
  ' >/dev/null 2>&1
}

if ! playwright_chromium_ready; then
  pnpm --filter web exec playwright install chromium
fi

if ! playwright_chromium_ready; then
  echo "[ERROR] Playwright Chromium executable could not be verified after installation." >&2
  exit 1
fi

export CADENCE_TEST_BROWSER=1
if [ -n "${CLAUDE_ENV_FILE:-}" ] && ! grep -qs 'CADENCE_TEST_BROWSER' "$CLAUDE_ENV_FILE" 2>/dev/null; then
  echo 'export CADENCE_TEST_BROWSER=1' >> "$CLAUDE_ENV_FILE"
fi
