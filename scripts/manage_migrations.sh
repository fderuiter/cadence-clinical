#!/usr/bin/env bash
# ==============================================================================
# Cadence Clinical Platform - EF Core PostgreSQL Migration Manager (POSIX)
# Requirements: PRD-SYS-053, ADR-2233, ADR-2447
# ==============================================================================

set -euo pipefail

export PATH="$PATH:$HOME/.dotnet/tools"

ACTION="${1:-}"
SERVICE="${2:-}"
MIGRATION_NAME="${3:-}"

if [ -z "$ACTION" ] || [ -z "$SERVICE" ]; then
    echo "Usage: $0 <add|script|bundle|apply|seed|list|optimize> <kernel|execution-module|intelligence|service-name|all> [migration-name]"
    exit 1
fi

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
KNOWN_SERVICES=(
    "kernel"
    "execution-module"
    "intelligence"
    "ctms"
    "econsent"
    "eisf"
    "etmf"
    "execution"
    "fileshare"
    "imaging"
    "interop"
    "knowledge"
    "notifications"
    "org"
    "payments"
    "quality"
    "safety"
    "tickets"
)

SERVICE_LOWER="$(echo "$SERVICE" | tr '[:upper:]' '[:lower:]')"

if [ "$SERVICE_LOWER" = "all" ] && [ "$ACTION" = "apply" ]; then
    echo -e "\n=== Cadence EF Core Migration Manager ==="
    echo "Target: All 18 DbContext assemblies (19 PostgreSQL schemas)"
    echo -e "Action: apply\n"
    dotnet run --project "$REPO_ROOT/packages/tooling_core/src/Cadence.DatabaseMigrator/Cadence.DatabaseMigrator.csproj"
    echo "[SUCCESS] Database update complete."
    exit 0
fi

if [ "$SERVICE_LOWER" = "all" ]; then
    SERVICES=("${KNOWN_SERVICES[@]}")
else
    SERVICES=("$SERVICE_LOWER")
fi

run_action_for_service() {
    cd "$REPO_ROOT"
    local service="$1"
    local cap_name
    local project_dir
    local project_path
    local migrations_dir
    local service_label

    case "$service" in
        kernel)
            # The Clinical Kernel keeps its migrations in Kernel.Persistence (ADR-2247, spec §2.4).
            project_dir="$REPO_ROOT/packages/kernel/src/Cadence.Kernel.Persistence"
            project_path="$project_dir/Cadence.Kernel.Persistence.csproj"
            migrations_dir="Migrations"
            service_label="Cadence.Kernel.Persistence"
            ;;
        execution-module|modules/execution)
            project_dir="$REPO_ROOT/apps/execution/module"
            project_path="$project_dir/Cadence.Modules.Execution.csproj"
            migrations_dir="Infrastructure/Migrations"
            service_label="Cadence.Modules.Execution"
            ;;
        intelligence|intelligence-module|modules/intelligence)
            project_dir="$REPO_ROOT/apps/intelligence/src"
            project_path="$project_dir/Cadence.Modules.Intelligence.csproj"
            migrations_dir="Infrastructure/Persistence/Migrations"
            service_label="Cadence.Modules.Intelligence"
            ;;
        *)
            cap_name="$(tr '[:lower:]' '[:upper:]' <<< "${service:0:1}")${service:1}"
            project_dir="$REPO_ROOT/apps/$service/src"
            project_path="$project_dir/Cadence.Apps.$cap_name.csproj"
            migrations_dir="Infrastructure/Persistence/Migrations"
            service_label="Cadence.Apps.$cap_name"
            ;;
    esac

    if [ ! -f "$project_path" ]; then
        echo "Error: Project not found at $project_path"
        exit 1
    fi

    echo -e "\n=== Cadence EF Core Migration Manager ==="
    echo "Service: $service_label"
    echo -e "Action:  $ACTION\n"

    case "$ACTION" in
        add)
            if [ -z "$MIGRATION_NAME" ]; then
                echo "Error: Migration name required for 'add'."
                exit 1
            fi
            echo "Generating migration '$MIGRATION_NAME'..."
            dotnet ef migrations add "$MIGRATION_NAME" --project "$project_path" --output-dir "$migrations_dir"
            echo "[SUCCESS] Migration created in $migrations_dir"
            ;;
        script)
            OUT_FILE="$project_dir/$migrations_dir/idempotent_migration.sql"
            echo "Generating idempotent SQL migration script..."
            dotnet ef migrations script --idempotent --project "$project_path" --output "$OUT_FILE"
            echo "[SUCCESS] SQL Script written to $OUT_FILE"
            ;;
        bundle)
            OUT_FILE="$project_dir/migration_bundle"
            echo "Generating self-contained executable migration bundle..."
            dotnet ef migrations bundle --project "$project_path" --output "$OUT_FILE" --force
            echo "[SUCCESS] Migration bundle created at $OUT_FILE"
            ;;
        apply)
            echo "Applying pending migrations to target database..."
            local conn_str="${ConnectionStrings__cadence:-${POSTGRES_CONNECTION_STRING:-}}"
            if [ -n "$conn_str" ]; then
                dotnet ef database update --project "$project_path" --connection "$conn_str" --no-build || dotnet ef database update --project "$project_path" --connection "$conn_str"
            else
                dotnet ef database update --project "$project_path" --no-build || dotnet ef database update --project "$project_path"
            fi
            echo "[SUCCESS] Database update complete."
            ;;
        seed)
            echo "Executing C# DbContext seeders for target service..."
            dotnet run --project "$project_path" --no-build -- --seed || dotnet run --project "$project_path" -- --seed
            echo "[SUCCESS] Seeding complete."
            ;;
        list)
            dotnet ef migrations list --project "$project_path"
            ;;
        optimize)
            echo "Generating pre-compiled EF Core models for target service..."
            dotnet ef dbcontext optimize --project "$project_path" --output-dir "CompiledModels" --namespace "$service_label.CompiledModels"
            echo "[SUCCESS] Compiled model optimization complete."
            ;;
        *)
            echo "Unknown action: $ACTION"
            exit 1
            ;;
    esac
}

for s in "${SERVICES[@]}"; do
    run_action_for_service "$s"
done
