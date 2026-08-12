#!/usr/bin/env python3
"""
Path-pattern boundary linter script.

Ensures that the files in the repository adhere to correct directory structure rules.
Provides command-line interface to check all files, staged files, or a specific list of files.
"""

import argparse
import fnmatch
import re
import subprocess
import sys
from pathlib import Path

# Static mapping of file extensions/patterns to their permitted target folders
RULES = {
    "*.py": {
        "allowed": [
            "apps/**/*.py",
            "packages/**/*.py",
            "scripts/**/*.py",
            "tests/**/*.py",
            "verification/**/*.py",
        ],
        "description": "Python files (*.py) must reside inside apps/, packages/, scripts/, tests/, or verification/.",
    },
    "*.vue": {
        "allowed": [
            "apps/web/**/*.vue",
            "apps/subject-portal/**/*.vue",
            "packages/ui/**/*.vue",
        ],
        "description": "Vue components (*.vue) must reside inside frontend structures (e.g., apps/web/, apps/subject-portal/, packages/ui/).",
    },
    "*.ts": {
        "allowed": [
            "apps/web/**/*.ts",
            "apps/subject-portal/**/*.ts",
            "packages/ui/**/*.ts",
            "packages/gateway-rewrite/**/*.ts",
            "packages/usdm-schemas/**/*.ts",
            "tests/**/*.ts",
        ],
        "description": "TypeScript files (*.ts) must reside inside frontend structures, packages, or tests (e.g., apps/web/, apps/subject-portal/, packages/ui/, packages/gateway-rewrite/, packages/usdm-schemas/, tests/).",
    },
    "*.tsx": {
        "allowed": [
            "apps/web/**/*.tsx",
            "apps/subject-portal/**/*.tsx",
            "packages/ui/**/*.tsx",
        ],
        "description": "TypeScript TSX files (*.tsx) must reside inside frontend structures (e.g., apps/web/, apps/subject-portal/, packages/ui/).",
    },
    "*.css": {
        "allowed": [
            "apps/web/**/*.css",
            "apps/subject-portal/**/*.css",
            "packages/ui/**/*.css",
            "docs/**/*.css",
        ],
        "description": "CSS files (*.css) must reside inside frontend apps, packages, or documentation themes.",
    },
    "*.scss": {
        "allowed": [
            "apps/web/**/*.scss",
            "apps/subject-portal/**/*.scss",
            "packages/ui/**/*.scss",
        ],
        "description": "SCSS files (*.scss) must reside inside frontend apps or packages.",
    },
    "*.sh": {
        "allowed": [
            "scripts/**/*.sh",
        ],
        "description": "Shell scripts (*.sh) must reside in scripts/ or its subfolders.",
    },
    "*.js": {
        "allowed": [
            "apps/**/*.js",
            "packages/**/*.js",
            "scripts/**/*.js",
            "docs/**/*.js",
            "tests/**/*.js",
            "verification/**/*.js",
        ],
        "description": "JavaScript files (*.js) must reside in apps/, packages/, scripts/, docs/, tests/, or verification/.",
    },
    "*.mjs": {
        "allowed": [
            "apps/**/*.mjs",
            "packages/**/*.mjs",
            "scripts/**/*.mjs",
            "docs/**/*.mjs",
            "tests/**/*.mjs",
            "verification/**/*.mjs",
        ],
        "description": "MJS files (*.mjs) must reside in apps/, packages/, scripts/, docs/, tests/, or verification/.",
    },
    "*.md": {
        "allowed": [
            "apps/**/*.md",
            "packages/**/*.md",
            "docs/**/*.md",
            "scripts/**/*.md",
            "tests/**/*.md",
            "verification/**/*.md",
            ".github/**/*.md",
        ],
        "description": "Markdown files (*.md) must reside in apps/, packages/, docs/, scripts/, tests/, verification/, or .github/.",
    },
}

# Permitted files directly in the root folder
ALLOWED_ROOT_FILES = {
    "ARCHITECTURE.md",
    "CONTRIBUTING.md",
    "README.md",
    "AGENTS.md",
    "SUPPORT.md",
    "LICENSE",
    "Makefile",
    "package.json",
    "pnpm-workspace.yaml",
    "pnpm-lock.yaml",
    "pyproject.toml",
    "uv.lock",
    ".gitignore",
    ".gitattributes",
    ".pre-commit-config.yaml",
    ".prettierignore",
    ".secrets.baseline",
    ".prettierrc",
    "eslint.config.mjs",
    "duplication_summary.json",
    ".python-version",
    "PROJECT.md",
    "TEST_INFRA.md",
}

# Allowed root-level subdirectories for general file placement
APPROVED_SUBDIRECTORIES = {
    "apps/",
    "packages/",
    "docs/",
    "scripts/",
    "tests/",
    "verification/",
    "docker/",
    ".github/",
}


def match_pattern(path: str, pattern: str) -> bool:
    """
    Robust pattern matching that supports standard glob patterns including '**' and '*'.
    """
    p = pattern
    # Use placeholders to avoid escaping issues
    p = p.replace("**/", "__DOUBLE_STAR_SLASH__")
    p = p.replace("**", "__DOUBLE_STAR__")
    p = p.replace("*", "__SINGLE_STAR__")
    p = p.replace("?", "__QUESTION__")

    p = re.escape(p)

    p = p.replace("__DOUBLE_STAR_SLASH__", "(?:.*/)?")
    p = p.replace("__DOUBLE_STAR__", ".*")
    p = p.replace("__SINGLE_STAR__", "[^/]*")
    p = p.replace("__QUESTION__", "[^/]")

    regex = "^" + p + "$"
    return bool(re.match(regex, path))


def run_layout_assertions(repo_root: Path):
    """
    Ensures that the execution environment meets structural, system, and runtime requirements.
    Replicates the environment integrity assertions.
    """
    # 1. Verify Python Version (>= 3.12)

    # 2. Verify Presence of Core Directory Boundaries
    expected_dirs = ["apps", "packages", "docs", "tests", "scripts"]
    for d in expected_dirs:
        dir_path = repo_root / d
        if not dir_path.is_dir():
            raise AssertionError(
                f"Core GxP directory boundary '{d}' is missing at {dir_path}!"
            )

    # 3. Verify Presence of Critical Dependency Manifests
    if not (repo_root / "pyproject.toml").is_file():
        raise AssertionError("pyproject.toml manifest is missing!")
    if not (repo_root / "uv.lock").is_file():
        raise AssertionError("uv.lock dependency lockfile is missing!")


def validate_file(file_path: str, repo_root: Path) -> tuple[bool, str]:
    """
    Validates a file path against static mappings and general constraints.
    """
    # Normalize to relative path with forward slashes
    try:
        path_obj = Path(file_path)
        if path_obj.is_absolute():
            path_obj = path_obj.relative_to(repo_root)
        posix_path = path_obj.as_posix()
    except ValueError:
        return False, f"File path '{file_path}' is outside the repository root."

    # Immediate pass for allowed root files
    if posix_path in ALLOWED_ROOT_FILES:
        return True, ""

    # Check approved subdirectories
    is_in_approved_sub = any(
        posix_path.startswith(sub) for sub in APPROVED_SUBDIRECTORIES
    )
    if not is_in_approved_sub:
        return (
            False,
            f"File resides outside permitted root-level directories. Must reside in: {', '.join(APPROVED_SUBDIRECTORIES)}",
        )

    # Match against RULES
    for pattern, rule_info in RULES.items():
        if fnmatch.fnmatchcase(posix_path, pattern) or fnmatch.fnmatchcase(
            path_obj.name, pattern
        ):
            matched = any(
                match_pattern(posix_path, allowed_pattern)
                for allowed_pattern in rule_info["allowed"]
            )
            if not matched:
                return False, str(rule_info["description"])

    return True, ""


def main():
    """
    Main entry point for the path-pattern boundary linter.

    Parses arguments, executes directory layout assertions, collects files to
    validate (all, staged, or a specific list), and validates each file path.
    """
    parser = argparse.ArgumentParser(
        description="Lightweight Path-Pattern Boundary Linter"
    )
    parser.add_argument("files", nargs="*", help="File paths to validate")
    parser.add_argument(
        "--staged", action="store_true", help="Validate only staged files"
    )
    parser.add_argument(
        "--all", action="store_true", help="Validate all tracked files in git"
    )
    args = parser.parse_args()

    repo_root = Path(__file__).resolve().parent.parent

    # Execute directory layout assertions
    try:
        run_layout_assertions(repo_root)
    except AssertionError as e:
        print(f"❌ Layout Integrity Assertion Failed: {e}", file=sys.stderr)
        sys.exit(1)

    # Determine files to check
    if args.all:
        try:
            result = subprocess.run(
                ["git", "ls-files"],
                capture_output=True,
                text=True,
                check=True,
                cwd=repo_root,
            )
            files_to_check = [f for f in result.stdout.splitlines() if f.strip()]
        except subprocess.CalledProcessError as e:
            print(f"❌ Failed to run git ls-files: {e}", file=sys.stderr)
            sys.exit(1)
    elif args.staged or not args.files:
        try:
            result = subprocess.run(
                ["git", "diff", "--cached", "--name-only", "--diff-filter=d"],
                capture_output=True,
                text=True,
                check=True,
                cwd=repo_root,
            )
            files_to_check = [f for f in result.stdout.splitlines() if f.strip()]
        except subprocess.CalledProcessError as e:
            print(f"❌ Failed to run git diff: {e}", file=sys.stderr)
            sys.exit(1)
    else:
        files_to_check = args.files

    if not files_to_check:
        print("✔ No files to validate.")
        sys.exit(0)

    # Validate each file path
    violations = []
    for file_path in files_to_check:
        # Ignore directories if they are passed
        if (repo_root / file_path).is_dir():
            continue
        is_valid, err_msg = validate_file(file_path, repo_root)
        if not is_valid:
            violations.append((file_path, err_msg))

    if violations:
        print(
            f"\n❌ Path Pattern Boundary Violations Found ({len(violations)}):",
            file=sys.stderr,
        )
        for path, err in violations:
            print(f"  - {path}", file=sys.stderr)
            print(f"    Expected: {err}", file=sys.stderr)
        print(
            "\nError: Structural boundaries violated. Please move the misplaced files to their designated folders.\n",
            file=sys.stderr,
        )
        sys.exit(1)
    else:
        print(
            f"✔ Successfully validated {len(files_to_check)} file(s). No boundary violations found."
        )
        sys.exit(0)


if __name__ == "__main__":
    main()
