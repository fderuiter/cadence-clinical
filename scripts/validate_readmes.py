#!/usr/bin/env python3
"""Static linter script to validate README documentation coverage and standard section headers.

Checks that every shared package under packages/ and microservice under apps/
contains a non-empty README.md file adhering to the five standard section headers:
1. Overview
2. Architectural Boundaries
3. Public APIs / Exports
4. Dependencies
5. Local Development & Testing
"""

import argparse
import sys
from pathlib import Path

REQUIRED_HEADERS = [
    "Overview",
    "Architectural Boundaries",
    "Public APIs / Exports",
    "Dependencies",
    "Local Development & Testing",
]


def find_target_directories(repo_root: Path) -> tuple[list[Path], list[Path]]:
    """Return lists of package and app directories to check."""
    packages_dir = repo_root / "packages"
    apps_dir = repo_root / "apps"

    package_dirs = []
    if packages_dir.exists():
        package_dirs = [
            d
            for d in packages_dir.iterdir()
            if d.is_dir() and not d.name.startswith(".") and d.name != "__pycache__"
        ]

    app_dirs = []
    if apps_dir.exists():
        app_dirs = [
            d
            for d in apps_dir.iterdir()
            if d.is_dir() and not d.name.startswith(".") and d.name != "__pycache__"
        ]

    return sorted(package_dirs), sorted(app_dirs)


def check_readme_file(dir_path: Path) -> list[str]:
    """Check a single module directory for a valid README.md file.

    Returns a list of error strings for this directory (empty if valid).
    """
    errors = []
    readme_path = dir_path / "README.md"

    if not readme_path.exists():
        return [
            f"Missing README.md in directory: {dir_path.relative_to(dir_path.parent.parent)}"
        ]

    try:
        content = readme_path.read_text(encoding="utf-8")
    except Exception as e:
        return [f"Could not read {readme_path}: {e}"]

    if not content.strip():
        return [
            f"Empty README.md in directory: {dir_path.relative_to(dir_path.parent.parent)}"
        ]

    # Extract all markdown headings (# ...)
    heading_lines = [
        line.strip() for line in content.splitlines() if line.strip().startswith("#")
    ]

    missing_headers = []
    for required_header in REQUIRED_HEADERS:
        header_found = any(
            required_header.lower() in line.lower() for line in heading_lines
        )
        if not header_found:
            missing_headers.append(required_header)

    if missing_headers:
        rel_path = dir_path.relative_to(dir_path.parent.parent)
        errors.append(
            f"Directory '{rel_path}' README.md is missing required section header(s): {', '.join(missing_headers)}"
        )

    return errors


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Validate README coverage and standard section headers."
    )
    parser.add_argument(
        "--repo-root",
        type=Path,
        default=Path(__file__).resolve().parent.parent,
        help="Path to repository root",
    )
    args = parser.parse_args()

    repo_root = args.repo_root.resolve()
    package_dirs, app_dirs = find_target_directories(repo_root)

    all_dirs = package_dirs + app_dirs
    if not all_dirs:
        print("Warning: No packages or apps directories found to validate.")
        return 0

    all_errors = []
    for module_dir in all_dirs:
        errors = check_readme_file(module_dir)
        all_errors.extend(errors)

    if all_errors:
        print("=" * 70, file=sys.stderr)
        print("    README Documentation Validation Failures", file=sys.stderr)
        print("=" * 70, file=sys.stderr)
        for error in all_errors:
            print(f"❌ {error}", file=sys.stderr)
        print("=" * 70, file=sys.stderr)
        print(f"Total validation errors: {len(all_errors)}", file=sys.stderr)
        print(
            "Run 'python scripts/scaffold_readmes.py' to generate compliant README files.",
            file=sys.stderr,
        )
        return 1

    print(
        f"✔ README Validation Passed: All {len(all_dirs)} packages and microservices contain compliant README.md files."
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
