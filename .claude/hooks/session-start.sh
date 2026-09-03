#!/bin/bash
# SessionStart hook: runs the repo's full-stack setup wizard (non-interactive,
# --minimal) so `dotnet`, `pnpm`, and `uv`/`cadence` commands all work in
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

bash scripts/setup_wizard.sh --dev --minimal
