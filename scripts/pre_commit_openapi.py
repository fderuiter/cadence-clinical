"""Pre-commit hook for automated OpenAPI schema regeneration and staging.

This script identifies if any API route files, Pydantic models, or schema
definitions have been modified. If so, it compiles and validates the OpenAPI
schemas and stages the regenerated JSON files in docs/openapi/.

Compliance:
- Gate 1: Google-style docstrings and clear comments.
- Gate 3: Part of pre-commit validation.
"""

import os
import subprocess
import sys
from pathlib import Path

# Add repository root to sys.path
REPO_ROOT = Path(__file__).resolve().parent.parent
if str(REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(REPO_ROOT))

# Enforce Python 3.14+ runtime before loading standard modules or packages
if sys.version_info < (3, 14):
    try:
        from scripts.runtime_guard import enforce_python_runtime

        enforce_python_runtime()
    except Exception:
        sys.stderr.write(
            f"[FATAL] Incompatible Python runtime {sys.version.split()[0]} ({sys.executable}).\n"
            "Cadence Clinical requires Python 3.14+.\n"
            "Please run: uv run python scripts/pre_commit_openapi.py\n"
        )
        sys.exit(1)

from scripts.runtime_guard import enforce_python_runtime


def get_staged_files() -> list[str]:
    """Retrieve the list of currently staged files in git.

    Returns:
        List[str]: A list of relative file paths of staged files.
    """
    try:
        result = subprocess.run(
            ["git", "diff", "--cached", "--name-only", "--diff-filter=ACM"],
            capture_output=True,
            text=True,
            check=True,
        )
        return [line.strip() for line in result.stdout.splitlines() if line.strip()]
    except (subprocess.SubprocessError, FileNotFoundError) as e:
        print(
            f"Warning: Failed to retrieve staged files from git: {e}", file=sys.stderr
        )
        return []


def should_trigger_schema_generation(staged_files: list[str]) -> bool:
    """Determine if any staged files should trigger OpenAPI regeneration.

    Args:
        staged_files: A list of staged file paths.

    Returns:
        bool: True if any staged file is an API route, Pydantic model, or schema
              definition, or any frontend API client change; False otherwise.
    """
    for f in staged_files:
        if "apps/web/src/api/" in f:
            return True
        if not f.endswith(".py"):
            continue
        parts = f.split(os.sep)
        # Check main.py in apps
        if len(parts) >= 3 and parts[0] == "apps" and parts[-1] == "main.py":
            return True
        # Check routers/ in apps
        if "routers" in parts:
            return True
        # Check models or schemas in path
        filename = parts[-1]
        if (
            "models.py" in filename
            or "schemas.py" in filename
            or "models" in parts
            or "schemas" in parts
        ):
            return True
    return False


def check_and_run_exporter() -> int:
    """Check Python environment, run the OpenAPI schema exporter, and stage files.

    Returns:
        int: Exit code (0 for success/bypass, non-zero for failure).
    """
    app_root = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))

    staged_files = get_staged_files()
    if not should_trigger_schema_generation(staged_files):
        print(
            "No API routes, Pydantic models, or schema definitions modified. Bypassing schema compilation."
        )
        return 0

    print("API or schema changes detected. Initiating local schema compilation...")

    # Determine Python executable path in the local virtual environment
    if os.name == "nt":
        python_path = os.path.join(app_root, ".venv", "Scripts", "python.exe")
    else:
        python_path = os.path.join(app_root, ".venv", "bin", "python")

    # Verify virtual environment existence and required packages
    venv_exists = os.path.exists(python_path)
    dependencies_installed = False

    if venv_exists:
        try:
            # Check if core dependencies are importable in the virtual environment
            check_result = subprocess.run(
                [python_path, "-c", "import fastapi, pydantic, sqlalchemy, neo4j"],
                capture_output=True,
            )
            if check_result.returncode == 0:
                dependencies_installed = True
        except subprocess.SubprocessError:
            pass

    if not venv_exists or not dependencies_installed:
        print("=" * 80, file=sys.stderr)
        print(
            "[ERROR] Git Pre-Commit Hook: Missing Required Python Dependencies!",
            file=sys.stderr,
        )
        print("=" * 80, file=sys.stderr)
        print(
            "The local Python virtual environment is either missing or does not have",
            file=sys.stderr,
        )
        print("all required microservice dependencies installed.", file=sys.stderr)
        print(
            "\nTo resolve this, please run one of the following commands in the workspace root:",
            file=sys.stderr,
        )
        print("    pnpm run setup:dev", file=sys.stderr)
        print("  or", file=sys.stderr)
        print("    uv sync --all-extras", file=sys.stderr)
        print(
            "\nThis will initialize the local python virtual environment (.venv) and install",
            file=sys.stderr,
        )
        print("all required microservice import dependencies.", file=sys.stderr)
        print("=" * 80, file=sys.stderr)
        return 1

    # Run the validation and export script using the virtual environment's python
    validate_script = os.path.join(app_root, "scripts", "validate_schemas.py")
    export_dir = os.path.join(app_root, "docs", "openapi")

    print(f"Running schema validation and exporting to '{export_dir}'...")
    try:
        export_result = subprocess.run(
            [python_path, validate_script, "--export-dir", export_dir],
            capture_output=True,
            text=True,
        )
        if export_result.returncode != 0:
            print("[ERROR] Schema validation or export failed:", file=sys.stderr)
            print(export_result.stdout, file=sys.stderr)
            print(export_result.stderr, file=sys.stderr)
            return export_result.returncode

        print(export_result.stdout)
    except subprocess.SubprocessError as e:
        print(f"[ERROR] Failed to run schema validation script: {e}", file=sys.stderr)
        return 1

    # Compile TypeScript definitions from the aggregated schema
    types_out_file = os.path.join(app_root, "apps", "web", "src", "api", "types.ts")
    print(
        f"Compiling aggregated OpenAPI Gateway schema into TypeScript: {types_out_file}..."
    )
    try:
        ts_compile_result = subprocess.run(
            [
                "pnpm",
                "--prefix",
                "apps/web",
                "dlx",
                "openapi-typescript@6.2.8",
                "docs/openapi/cdisc_openapi.json",
                "-o",
                "apps/web/src/api/types.ts",
            ],
            cwd=app_root,
            capture_output=True,
            text=True,
        )
        if ts_compile_result.returncode != 0:
            print(
                "[ERROR] TypeScript compilation of Gateway schema failed:",
                file=sys.stderr,
            )
            print(ts_compile_result.stdout, file=sys.stderr)
            print(ts_compile_result.stderr, file=sys.stderr)
            return ts_compile_result.returncode
        print("TypeScript types successfully compiled!")
    except subprocess.SubprocessError as e:
        print(
            f"[ERROR] Failed to compile OpenAPI Gateway schema into TypeScript: {e}",
            file=sys.stderr,
        )
        return 1

    # Run TypeScript typecheck on the web workspace
    print("Running TypeScript typecheck to verify contract alignment...")
    try:
        typecheck_result = subprocess.run(
            ["pnpm", "--filter", "web", "typecheck"],
            cwd=app_root,
            capture_output=True,
            text=True,
        )
        if typecheck_result.returncode != 0:
            print(
                "[ERROR] TypeScript typecheck failed! Drift detected between backend definitions and manual clients.",
                file=sys.stderr,
            )
            print(typecheck_result.stdout, file=sys.stderr)
            print(typecheck_result.stderr, file=sys.stderr)
            return typecheck_result.returncode
        print("TypeScript typecheck validation passed successfully!")
    except subprocess.SubprocessError as e:
        print(f"[ERROR] Failed to execute TypeScript typecheck: {e}", file=sys.stderr)
        return 1

    # Automatically stage any newly generated OpenAPI JSON and TypeScript type changes
    print(
        "Staging newly compiled OpenAPI schemas and TypeScript types into the current commit..."
    )
    try:
        subprocess.run(
            ["git", "add", export_dir, types_out_file],
            cwd=app_root,
            check=True,
        )
        print("OpenAPI schemas and TypeScript types successfully compiled and staged!")
    except subprocess.SubprocessError as e:
        print(
            f"Warning: Failed to automatically stage changes: {e}",
            file=sys.stderr,
        )

    return 0


if __name__ == "__main__":
    sys.exit(check_and_run_exporter())
