#!/usr/bin/env python3
"""OpenAPI Specification & API Documentation Drift Validation Sentinel.

Executes offline validation across:
1. docs/SDLC/03_API_Integration_Specification.md (via scripts/sync_openapi_spec.py --validate)
2. docs/api/*.md and docs/api/index.md (via scripts/generate_api_docs.py --validate)

Returns exit code 0 if all specs match active route definitions, or 1 if drift is detected.
"""

import subprocess
import sys
from pathlib import Path

# Ensure repository root is in python path
ROOT_DIR = Path(__file__).resolve().parent.parent
if str(ROOT_DIR) not in sys.path:
    sys.path.insert(0, str(ROOT_DIR))

from scripts.runtime_guard import enforce_python_runtime, print_runtime_info


def main() -> None:
    enforce_python_runtime(script_name="validate_openapi_drift")
    print_runtime_info("validate_openapi_drift.py")
    print("--- Running OpenAPI Specification & Documentation Drift Sentinel ---")

    python_exec = sys.executable

    # 1. Validate SDLC API Integration Specification file
    sync_script = ROOT_DIR / "scripts" / "sync_openapi_spec.py"
    print("\n[GATE 1/2] Checking docs/SDLC/03_API_Integration_Specification.md...")
    res_spec = subprocess.run(
        [python_exec, str(sync_script), "--validate"],
        capture_output=True,
        text=True,
    )
    if res_spec.stdout:
        print(res_spec.stdout.strip())
    if res_spec.stderr:
        print(res_spec.stderr.strip(), file=sys.stderr)

    # 2. Validate docs/api/*.md Markdown documentation files
    gen_docs_script = ROOT_DIR / "scripts" / "generate_api_docs.py"
    print("\n[GATE 2/2] Checking docs/api/*.md documentation parity...")
    res_docs = subprocess.run(
        [python_exec, str(gen_docs_script), "--validate"],
        capture_output=True,
        text=True,
    )
    if res_docs.stdout:
        print(res_docs.stdout.strip())
    if res_docs.stderr:
        print(res_docs.stderr.strip(), file=sys.stderr)

    if res_spec.returncode != 0 or res_docs.returncode != 0:
        print(
            "\n" + "=" * 80,
            file=sys.stderr,
        )
        print(
            "[ERROR] OpenAPI Specification Drift Detected!",
            file=sys.stderr,
        )
        print(
            "=" * 80,
            file=sys.stderr,
        )
        print(
            "Published API documentation has drifted from backend routes/models.",
            file=sys.stderr,
        )
        print(
            "To automatically resynchronize specifications and documentation, run:",
            file=sys.stderr,
        )
        print("    make fix", file=sys.stderr)
        print("  or", file=sys.stderr)
        print("    uv run cadence fix --all", file=sys.stderr)
        print("=" * 80 + "\n", file=sys.stderr)
        sys.exit(1)

    print("\n[SUCCESS] OpenAPI Specification Drift Sentinel passed cleanly.")
    sys.exit(0)


if __name__ == "__main__":
    main()
