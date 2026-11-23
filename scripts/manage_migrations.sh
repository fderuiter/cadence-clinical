#!/usr/bin/env bash
# ==============================================================================
# Cadence Clinical Platform - EF Core PostgreSQL Migration Manager (POSIX)
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

run_cadence_migrations() {
    cd "$REPO_ROOT"
    dotnet run --project "$CLI_PROJECT" -- migrations "$@"
}

case "$ACTION" in
    add)
        if [ -z "$SERVICE" ] || [ -z "$MIGRATION_NAME" ]; then
            echo "Usage: $0 add <module-name> <migration-name>"
            exit 1
        fi
        run_cadence_migrations add "$SERVICE" "$MIGRATION_NAME"
        exit $?
        ;;
    script)
        run_cadence_migrations script "${SERVICE:-all}"
        exit $?
        ;;
    list)
        run_cadence_migrations list "${SERVICE:-all}"
        exit $?
        ;;
    verify)
        run_cadence_migrations verify "${SERVICE:-all}"
        exit $?
        ;;
esac

if [ -z "$SERVICE" ]; then
    echo "Usage: $0 <bundle|apply|seed|optimize> <module-name|all>"
    exit 1
fi

KNOWN_SERVICES=(
    "kernel" "executionmodule" "intelligence"
    "ctms" "econsent" "eisf" "etmf" "execution" "fileshare" "imaging"
    "interop" "knowledge" "notifications" "org" "payments" "quality" "safety" "tickets"
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
    local compiled_ns

    case "$service" in
        kernel)
            # The Clinical Kernel keeps its migrations in Kernel.Persistence (ADR-2247, spec §2.4).
            project_dir="$REPO_ROOT/packages/kernel/src/Cadence.Kernel.Persistence"
            project_path="$project_dir/Cadence.Kernel.Persistence.csproj"
            compiled_ns="Cadence.Kernel.Persistence.CompiledModels"
            ;;
        executionmodule|execution-module|execution_module|modules/execution)
            project_dir="$REPO_ROOT/apps/execution/module"
            project_path="$project_dir/Cadence.Modules.Execution.csproj"
            compiled_ns="Cadence.Modules.Execution.CompiledModels"
            ;;
        intelligence|intelligence-module|intelligence_module|modules/intelligence)
            project_dir="$REPO_ROOT/apps/intelligence/src"
            project_path="$project_dir/Cadence.Modules.Intelligence.csproj"
            compiled_ns="Cadence.Modules.Intelligence.CompiledModels"
            ;;
        *)
            cap_name="$(tr '[:lower:]' '[:upper:]' <<< "${service:0:1}")${service:1}"
            project_dir="$REPO_ROOT/apps/$service/src"
            project_path="$project_dir/Cadence.Apps.$cap_name.csproj"
            compiled_ns="Cadence.Apps.$cap_name.CompiledModels"
            ;;
    esac

    if [ ! -f "$project_path" ]; then
        echo "Error: Project not found at $project_path"
        exit 1
    fi

    echo -e "\n=== Cadence EF Core Migration Manager ==="
    echo "Module:  $service ($project_path)"
    echo -e "Action:  $ACTION\n"

    case "$ACTION" in
        bundle)
            OUT_FILE="$REPO_ROOT/TestResults/migrations/${service}_migration_bundle"
            mkdir -p "$(dirname "$OUT_FILE")"
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
        optimize)
            echo "Generating pre-compiled EF Core models for target module..."
            dotnet ef dbcontext optimize --project "$project_path" --output-dir "CompiledModels" --namespace "$compiled_ns"
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
