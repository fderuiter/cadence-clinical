"""OpenAPI Documentation Drift Verification Sentinel.

This script compiles microservice OpenAPI schemas and the aggregated gateway specification,
re-exports all OpenAPI JSON files into docs/openapi/, updates docs/SDLC/03_API_Integration_Specification.md,
and asserts zero uncommitted file changes in API documentation files.

If drift is detected, the script reports the discrepancies, reverts any uncommitted modifications in
the documentation files to remain non-destructive to the runner environment, and fails with a non-zero exit code.

Requirements: PRD-SYS-001
"""

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
            "Please run: uv run python scripts/validate_openapi_drift.py\n"
        )
        sys.exit(1)

from scripts.runtime_guard import enforce_python_runtime, print_runtime_info


TARGET_DOC_PATHS = [
    "docs/openapi",
    "docs/SDLC/03_API_Integration_Specification.md",
]


def check_openapi_drift() -> bool:
    """Validate schemas, recompile API specifications, and assert zero documentation drift.

    Returns:
        bool: True if documentation is 100% in sync with code; False if drift or validation error occurred.
    """
    print_runtime_info("validate_openapi_drift.py")
    print("--- Starting Inline OpenAPI Documentation Drift Check ---")

    # Step 1: Validate microservice OpenAPI schemas and export to docs/openapi/
    print("\n[STEP 1] Validating microservice OpenAPI schemas and exporting JSON specs...")
    export_script = REPO_ROOT / "scripts" / "validate_schemas.py"
    export_dir = REPO_ROOT / "docs" / "openapi"

    res_export = subprocess.run(
        [sys.executable, str(export_script), "--export-dir", str(export_dir)],
        cwd=str(REPO_ROOT),
        capture_output=True,
        text=True,
    )

    if res_export.returncode != 0:
        print("\n[FAIL] OpenAPI Schema Validation failed:", file=sys.stderr)
        print(res_export.stdout, file=sys.stderr)
        print(res_export.stderr, file=sys.stderr)
        return False

    print("✔ Microservice OpenAPI schemas validated and exported successfully.")

    # Step 2: Synchronize unified OpenAPI spec in 03_API_Integration_Specification.md
    print("\n[STEP 2] Synchronizing unified OpenAPI spec into Markdown documentation...")
    sync_script = REPO_ROOT / "scripts" / "sync_openapi_spec.py"

    res_sync = subprocess.run(
        [sys.executable, str(sync_script)],
        cwd=str(REPO_ROOT),
        capture_output=True,
        text=True,
    )

    if res_sync.returncode != 0:
        print("\n[FAIL] Unified OpenAPI specification synchronization failed:", file=sys.stderr)
        print(res_sync.stdout, file=sys.stderr)
        print(res_sync.stderr, file=sys.stderr)
        return False

    print("✔ Unified OpenAPI specification synchronized successfully.")

    # Step 3: Assert zero uncommitted file changes in API documentation files
    print("\n[STEP 3] Asserting zero uncommitted API documentation drift...")
    res_status = subprocess.run(
        ["git", "status", "--porcelain", *TARGET_DOC_PATHS],
        cwd=str(REPO_ROOT),
        capture_output=True,
        text=True,
    )

    changed_output = res_status.stdout.strip()

    if changed_output:
        # Capture the git diff for diagnostic reporting
        res_diff = subprocess.run(
            ["git", "diff", *TARGET_DOC_PATHS],
            cwd=str(REPO_ROOT),
            capture_output=True,
            text=True,
        )
        diff_text = res_diff.stdout.strip()

        # Non-destructive cleanup: Revert modifications so workspace runner is left clean
        subprocess.run(
            ["git", "checkout", "--", *TARGET_DOC_PATHS],
            cwd=str(REPO_ROOT),
            capture_output=True,
            text=True,
        )
        subprocess.run(
            ["git", "clean", "-fd", "docs/openapi/"],
            cwd=str(REPO_ROOT),
            capture_output=True,
            text=True,
        )

        print(
            "\n::error::OpenAPI Documentation Drift Detected!",
            file=sys.stderr,
        )
        print(
            "Backend API route definitions or Pydantic schemas have been modified without updating the published API documentation.",
            file=sys.stderr,
        )
        print("\nModified documentation files:", file=sys.stderr)
        for line in changed_output.splitlines():
            print(f"  {line}", file=sys.stderr)

        if diff_text:
            print("\nDiff summary:", file=sys.stderr)
            for line in diff_text.splitlines()[:50]:
                print(f"  {line}", file=sys.stderr)
            if len(diff_text.splitlines()) > 50:
                print("  ... (truncated)", file=sys.stderr)

        print(
            "\nTo resolve this drift locally, please run:\n"
            "  uv run python scripts/validate_schemas.py --export-dir docs/openapi\n"
            "  uv run python scripts/sync_openapi_spec.py\n"
            "or simply:\n"
            "  uv run cadence fix --all\n"
            "and commit the updated API documentation files.",
            file=sys.stderr,
        )
        return False

    print("\n[VALIDATION PASSED] API documentation files match the backend OpenAPI schemas. Zero drift detected.")
    return True


def main() -> None:
    success = check_openapi_drift()
    sys.exit(0 if success else 1)


if __name__ == "__main__":
    main()
