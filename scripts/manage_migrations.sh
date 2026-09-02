#!/usr/bin/env bash
# ==============================================================================
# Cadence Clinical Platform - EF Core PostgreSQL Migration Manager (POSIX)
# Requirements: PRD-SYS-053, ADR-2233
# ==============================================================================

set -euo pipefail

ACTION="${1:-}"
SERVICE="${2:-}"
MIGRATION_NAME="${3:-}"

if [ -z "$ACTION" ] || [ -z "$SERVICE" ]; then
    echo "Usage: $0 <add|script|bundle|apply|list> <service-name> [migration-name]"
    exit 1
fi

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
CAP_NAME="$(tr '[:lower:]' '[:upper:]' <<< "${SERVICE:0:1}")${SERVICE:1}"
PROJECT_PATH="$REPO_ROOT/apps/$SERVICE/src/Cadence.Apps.$CAP_NAME.csproj"
MIGRATIONS_DIR="Infrastructure/Persistence/Migrations"

if [ ! -f "$PROJECT_PATH" ]; then
    echo "Error: Project not found at $PROJECT_PATH"
    exit 1
fi

echo -e "\n=== Cadence EF Core Migration Manager ==="
echo "Service: Cadence.Apps.$CAP_NAME"
echo -e "Action:  $ACTION\n"

case "$ACTION" in
    add)
        if [ -z "$MIGRATION_NAME" ]; then
            echo "Error: Migration name required for 'add'."
            exit 1
        fi
        echo "Generating migration '$MIGRATION_NAME'..."
        dotnet ef migrations add "$MIGRATION_NAME" --project "$PROJECT_PATH" --output-dir "$MIGRATIONS_DIR"
        echo "[SUCCESS] Migration created in $MIGRATIONS_DIR"
        ;;
    script)
        OUT_FILE="$REPO_ROOT/apps/$SERVICE/src/$MIGRATIONS_DIR/idempotent_migration.sql"
        echo "Generating idempotent SQL migration script..."
        dotnet ef migrations script --idempotent --project "$PROJECT_PATH" --output "$OUT_FILE"
        echo "[SUCCESS] SQL Script written to $OUT_FILE"
        ;;
    bundle)
        OUT_FILE="$REPO_ROOT/apps/$SERVICE/src/Infrastructure/Persistence/migration_bundle"
        echo "Generating self-contained executable migration bundle..."
        dotnet ef migrations bundle --project "$PROJECT_PATH" --output "$OUT_FILE" --force
        echo "[SUCCESS] Migration bundle created at $OUT_FILE"
        ;;
    apply)
        echo "Applying pending migrations to target database..."
        dotnet ef database update --project "$PROJECT_PATH"
        echo "[SUCCESS] Database update complete."
        ;;
    list)
        dotnet ef migrations list --project "$PROJECT_PATH"
        ;;
    *)
        echo "Unknown action: $ACTION"
        exit 1
        ;;
esac
