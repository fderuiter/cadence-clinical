#!/usr/bin/env bash
# ==============================================================================
# Cadence Clinical Platform - EF Core PostgreSQL Migration Manager (POSIX)
# Thin wrapper forwarding to `cadence db` (Requirements: PRD-SYS-053, ADR-2498)
# ==============================================================================

set -euo pipefail

ACTION="${1:-}"
TARGET="${2:-}"
MIGRATION_NAME="${3:-}"

if [ -z "$ACTION" ] || [ -z "$TARGET" ]; then
    echo "Usage: $0 <add|script|bundle|apply|seed|list|optimize> <module-name|all> [migration-name]" >&2
    exit 1
fi

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
CLI_PROJECT="$REPO_ROOT/packages/cli/src/Cadence.Cli.csproj"

case "$ACTION" in
    apply)
        exec dotnet run --project "$CLI_PROJECT" -- db migrate "$TARGET"
        ;;
    add)
        if [ -z "$MIGRATION_NAME" ]; then
            echo "Error: Migration name required for 'add'." >&2
            exit 1
        fi
        exec dotnet run --project "$CLI_PROJECT" -- db add "$TARGET" "$MIGRATION_NAME"
        ;;
    script|bundle|seed|list|optimize)
        exec dotnet run --project "$CLI_PROJECT" -- db "$ACTION" "$TARGET"
        ;;
    *)
        echo "Unknown action: $ACTION" >&2
        exit 1
        ;;
esac
