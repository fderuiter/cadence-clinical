#!/bin/bash
# SessionStart hook: installs the .NET 10 SDK (per global.json) so `dotnet`
# commands work in Claude Code on the web sessions. Only runs remotely.
set -euo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

cd "$CLAUDE_PROJECT_DIR"

if ! command -v dotnet >/dev/null 2>&1 || ! dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
  apt-get update -qq
  apt-get install -y --no-install-recommends dotnet-sdk-10.0
fi

DOTNET_TOOLS_DIR="${HOME}/.dotnet/tools"
mkdir -p "$DOTNET_TOOLS_DIR"
export PATH="$PATH:$DOTNET_TOOLS_DIR"
if [ -n "${CLAUDE_ENV_FILE:-}" ] && ! grep -qs '.dotnet/tools' "$CLAUDE_ENV_FILE" 2>/dev/null; then
  echo "export PATH=\"\$PATH:$DOTNET_TOOLS_DIR\"" >> "$CLAUDE_ENV_FILE"
fi

# Global tools used across the repo (dotnet-ef for migrations, dotnet-format for lint)
dotnet tool install --global dotnet-ef >/dev/null 2>&1 || dotnet tool update --global dotnet-ef >/dev/null 2>&1 || true
dotnet tool install --global dotnet-format >/dev/null 2>&1 || dotnet tool update --global dotnet-format >/dev/null 2>&1 || true

# Warm the NuGet cache; some projects currently fail to restore due to
# pinned preview package versions in Directory.Packages.props (pre-existing
# issue, unrelated to SDK setup) so don't fail the hook on restore errors.
dotnet restore Cadence.sln || true
