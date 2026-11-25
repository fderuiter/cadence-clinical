#!/usr/bin/env bash
# ==============================================================================
# Cadence Clinical Platform - EF Core PostgreSQL Migration Manager (POSIX)
# Thin pass-through wrapper around `cadence migrations` across all 18 modules.
# Requirements: PRD-SYS-053, ADR-2233, ADR-2447, ADR-2536
# ==============================================================================

set -euo pipefail

export PATH="$PATH:$HOME/.dotnet/tools"

ACTION="${1:-}"
SERVICE="${2:-}"
MIGRATION_NAME="${3:-}"

if [ -z "$ACTION" ]; then
    echo "Usage: $0 <add|script|verify|list|bundle|apply|seed|optimize> [module-name|all] [migration-name]"
    exit 1
fi

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
CLI_PROJECT="$REPO_ROOT/packages/cli/src/Cadence.Cli.csproj"

cd "$REPO_ROOT"

case "$ACTION" in
    add)
        if [ -z "$SERVICE" ] || [ -z "$MIGRATION_NAME" ]; then
            echo "Usage: $0 add <module-name> <migration-name>"
            exit 1
        fi
        exec dotnet run --project "$CLI_PROJECT" -- migrations add "$SERVICE" "$MIGRATION_NAME" "${@:4}"
        ;;
    script|verify|list|bundle|apply|seed|optimize)
        if [ -n "$SERVICE" ]; then
            exec dotnet run --project "$CLI_PROJECT" -- migrations "$ACTION" "$SERVICE" "${@:3}"
        else
            exec dotnet run --project "$CLI_PROJECT" -- migrations "$ACTION" all
        fi
        ;;
    *)
        echo "Unknown action: $ACTION"
        echo "Usage: $0 <add|script|verify|list|bundle|apply|seed|optimize> [module-name|all] [migration-name]"
        exit 1
        ;;
esac
