#!/usr/bin/env python3
"""AST contract validator and API client linter script.

Parses frontend API call sites under apps/web/src/api/ and verifies:
1. Zero explicit `any` type casts exist in frontend API clients (: any, as any, <any>).
2. All endpoint paths referenced in API clients match valid OpenAPI routes in docs/openapi/cdisc_openapi.json.

Compliance:
- Gate 1: Google-style docstrings and clear comments.
- Gate 3: Part of pre-commit validation.
"""

import argparse
import json
import re
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
            "Please run: uv run python scripts/validate_api_clients.py\n"
        )
        sys.exit(1)

from scripts.runtime_guard import enforce_python_runtime, print_runtime_info


def load_openapi_paths(spec_path: Path) -> set[str]:
    """Load all valid endpoint paths from the OpenAPI specification JSON file.

    Args:
        spec_path (Path): Path to cdisc_openapi.json.

    Returns:
        set[str]: Set of endpoint path strings (e.g., '/api/v1/studies/{study_id}').
    """
    if not spec_path.exists():
        sys.stderr.write(f"[ERROR] OpenAPI spec file not found at {spec_path}\n")
        return set()

    with open(spec_path, encoding="utf-8") as fh:
        spec = json.load(fh)

    paths = spec.get("paths", {})
    return set(paths.keys())


def check_for_any_type_casts(api_dir: Path) -> list[str]:
    """Check frontend API client files for explicit `any` type casts.

    Scans for patterns like `: any`, `as any`, `<any>`.

    Args:
        api_dir (Path): Path to the apps/web/src/api directory.

    Returns:
        list[str]: List of violation messages if any explicit `any` types are found.
    """
    violations = []
    any_regex = re.compile(r"(\b\w+\s*:\s*any\b|\bas\s+any\b|<any>)")

    client_files = list(api_dir.glob("*.ts")) + list(api_dir.glob("*.js"))
    for file_path in client_files:
        with open(file_path, encoding="utf-8") as fh:
            for line_no, line in enumerate(fh, 1):
                if any_regex.search(line):
                    rel_path = file_path.relative_to(REPO_ROOT)
                    violations.append(
                        f"{rel_path}:{line_no}: Found explicit `any` type cast -> {line.strip()}"
                    )

    return violations


def extract_called_api_paths(api_dir: Path) -> set[tuple[str, Path, int]]:
    """Extract string literal API paths referenced in frontend API client modules.

    Args:
        api_dir (Path): Path to the apps/web/src/api directory.

    Returns:
        set[tuple[str, Path, int]]: Set of tuples containing (path_string, file_path, line_number).
    """
    called_paths = set()
    # Match path strings like "/api/v1/..." in openApiClient calls or string literals
    path_regex = re.compile(r'["\'](/api/v1/[a-zA-Z0-9_/{}\-\.]+)["\']')

    client_files = list(api_dir.glob("*.ts")) + list(api_dir.glob("*.js"))
    for file_path in client_files:
        if file_path.name == "types.ts":
            continue
        with open(file_path, encoding="utf-8") as fh:
            for line_no, line in enumerate(fh, 1):
                for match in path_regex.finditer(line):
                    raw_path = match.group(1)
                    called_paths.add((raw_path, file_path, line_no))

    return called_paths


def validate_api_clients(spec_path: Path, api_dir: Path, strict: bool = True) -> int:
    """Validate frontend API clients against OpenAPI specification and lint guidelines.

    Args:
        spec_path (Path): Path to OpenAPI specification file.
        api_dir (Path): Path to web API client directory.
        strict (bool): Whether to enforce strict failure on contract violations.

    Returns:
        int: 0 if validation passes, 1 if violations are found.
    """
    print_runtime_info("AST API Client Contract Validator")

    openapi_paths = load_openapi_paths(spec_path)
    if not openapi_paths:
        return 1

    print(
        f"[+] Loaded {len(openapi_paths)} OpenAPI paths from {spec_path.relative_to(REPO_ROOT)}"
    )

    # 1. Check for explicit `any` type casts
    any_violations = check_for_any_type_casts(api_dir)

    # 2. Extract and check called API paths
    called_paths = extract_called_api_paths(api_dir)
    invalid_path_violations = []

    for raw_path, file_path, line_no in called_paths:
        if raw_path not in openapi_paths:
            rel_path = file_path.relative_to(REPO_ROOT)
            invalid_path_violations.append(
                f"{rel_path}:{line_no}: Referenced API path '{raw_path}' does not exist in OpenAPI spec."
            )

    has_errors = False

    if any_violations:
        has_errors = True
        sys.stderr.write(
            f"\n[FAIL] Found {len(any_violations)} explicit `any` type casts in {api_dir.relative_to(REPO_ROOT)}:\n"
        )
        for v in any_violations:
            sys.stderr.write(f"  - {v}\n")

    if invalid_path_violations:
        has_errors = True
        sys.stderr.write(
            f"\n[FAIL] Found {len(invalid_path_violations)} unmapped or invalid API path references:\n"
        )
        for v in invalid_path_violations:
            sys.stderr.write(f"  - {v}\n")

    if has_errors:
        sys.stderr.write("\n[FATAL] Frontend API client contract validation failed!\n")
        return 1 if strict else 0

    print(
        f"[PASS] Successfully verified {len(called_paths)} API call sites across {api_dir.relative_to(REPO_ROOT)}."
    )
    print("[PASS] Zero explicit `any` type assertions detected in API client modules.")
    return 0


def main() -> None:
    """Main CLI entrypoint for API client contract validation."""
    parser = argparse.ArgumentParser(
        description="Validate frontend openapi-fetch API clients against OpenAPI contract and TypeScript safety rules."
    )
    parser.add_argument(
        "--openapi-spec",
        type=Path,
        default=REPO_ROOT / "docs" / "openapi" / "cdisc_openapi.json",
        help="Path to cdisc_openapi.json specification.",
    )
    parser.add_argument(
        "--api-dir",
        type=Path,
        default=REPO_ROOT / "apps" / "web" / "src" / "api",
        help="Path to frontend API client directory.",
    )
    parser.add_argument(
        "--strict",
        action="store_true",
        default=True,
        help="Enforce strict exit code 1 on errors.",
    )

    args = parser.parse_args()
    sys.exit(validate_api_clients(args.openapi_spec, args.api_dir, args.strict))


if __name__ == "__main__":
    main()
