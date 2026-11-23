#!/usr/bin/env bash
# ==============================================================================
# Cadence Clinical Platform - EF Core PostgreSQL Migration Manager (POSIX)
# Thin wrapper forwarding to `cadence db` and `cadence migrations`
# (Requirements: PRD-SYS-053, ADR-2498, ADR-2536)
# ==============================================================================

set -euo pipefail

ACTION="${1:-}"
MODULE="${2:-}"
MIGRATION_NAME="${3:-}"

if [ -z "$ACTION" ]; then
    echo "Usage: $0 <add|script|verify|list|bundle|apply|seed|optimize> [module-name|all] [migration-name]" >&2
    exit 1
fi

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
CLI_PROJECT="$REPO_ROOT/packages/cli/src/Cadence.Cli.csproj"
TARGET="${MODULE:-all}"

case "$ACTION" in
    add)
        if [ -z "$MODULE" ] || [ -z "$MIGRATION_NAME" ]; then
            echo "Usage: $0 add <module-name> <migration-name>" >&2
            exit 1
        fi
        exec dotnet run --project "$CLI_PROJECT" -- db add "$MODULE" "$MIGRATION_NAME"
        ;;
    verify)
        exec dotnet run --project "$CLI_PROJECT" -- migrations verify "$TARGET"
        ;;
    apply)
        exec dotnet run --project "$CLI_PROJECT" -- db migrate "$TARGET"
        ;;
    script|bundle|seed|list|optimize)
        exec dotnet run --project "$CLI_PROJECT" -- db "$ACTION" "$TARGET"
        ;;
    *)
        echo "Unknown action: $ACTION" >&2
        exit 1
        ;;
esac
