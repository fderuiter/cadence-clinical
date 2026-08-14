#!/usr/bin/env python3
"""
validate_codeowners.py

CI validation script to ensure 100% code ownership coverage of all subdirectories
under clinical service boundaries (apps/ and packages/). This implements the Zero-Fallback
policy, requiring every service directory to map to an active domain-specific GitHub
organizational team.
"""

import os
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
            "Please run: uv run python scripts/validate_codeowners.py\n"
        )
        sys.exit(1)

from scripts.runtime_guard import enforce_python_runtime

# Regex pattern for valid organizational teams: @org-name/team-name
TEAM_PATTERN = re.compile(r"^@[a-zA-Z0-9\-_\.]+/[a-zA-Z0-9\-_\.]+$")


def is_valid_team(owner: str) -> bool:
    """Checks if a given owner handle is a valid GitHub organizational team."""
    return bool(TEAM_PATTERN.match(owner))


def normalize_path(p: str) -> str:
    """
    Normalizes a CODEOWNERS pattern or directory path into a canonical form
    suitable for exact string comparison (e.g. 'apps/compliance').
    """
    p = p.strip()
    p = p.replace("\\", "/")
    if p.startswith("/"):
        p = p[1:]
    while p.endswith("*"):
        p = p[:-1]
    if p.endswith("/"):
        p = p[:-1]
    return p.strip("/")


def main() -> None:
    codeowners_path = ".github/CODEOWNERS"
    if not os.path.exists(codeowners_path):
        print(f"Error: {codeowners_path} does not exist.")
        sys.exit(1)

    # 1. Dynamically find all subdirectories under apps/ and packages/
    dirs_to_check = ["apps", "packages"]
    subdirs = []
    for parent in dirs_to_check:
        if os.path.exists(parent):
            for name in os.listdir(parent):
                full_path = os.path.join(parent, name)
                if os.path.isdir(full_path):
                    # Exclude python/CI/IDE cache and configuration dirs
                    if (
                        name.startswith(".")
                        or name.startswith("__")
                        or name == "node_modules"
                    ):
                        continue
                    subdirs.append(full_path.replace("\\", "/"))

    print(
        f"Discovered {len(subdirs)} clinical boundary subdirectories under apps/ and packages/:"
    )
    for d in sorted(subdirs):
        print(f"  - {d}")
    print()

    # 2. Parse CODEOWNERS mapping rules
    rules = []
    try:
        with open(codeowners_path, encoding="utf-8") as f:
            for line in f:
                # Remove comments and strip whitespace
                line = line.strip()
                if not line or line.startswith("#"):
                    continue
                line = line.split(" #", 1)[0].strip()
                line = line.split("#", 1)[0].strip()
                if not line:
                    continue

                parts = line.split()
                if not parts:
                    continue
                pattern = parts[0]
                owners = parts[1:]
                rules.append({"pattern": pattern, "owners": owners})
    except Exception as e:
        print(f"Error reading/parsing CODEOWNERS file: {e}")
        sys.exit(1)

    # 3. Assert exact mapping coverage (Zero-Fallback) and team format constraints
    errors = []
    for subdir in sorted(subdirs):
        subdir_normalized = normalize_path(subdir)
        # Find all rules matching this subdir exactly in normalized form
        matching_rules = [
            r for r in rules if normalize_path(r["pattern"]) == subdir_normalized
        ]

        if not matching_rules:
            errors.append(
                f"Directory '{subdir}' lacks an explicit mapping in {codeowners_path}. "
                f"It must be explicitly assigned to a domain team."
            )
            continue

        for rule in matching_rules:
            pattern = rule["pattern"]
            owners = rule["owners"]
            if not owners:
                errors.append(
                    f"Rule for '{subdir}' ('{pattern}') has no owners defined."
                )
                continue

            for owner in owners:
                if not is_valid_team(owner):
                    errors.append(
                        f"Directory '{subdir}' is mapped to '{owner}' under rule '{pattern}'. "
                        f"Owner must be a valid GitHub organizational team (format: @org-name/team-name), "
                        f"NOT an individual user account (e.g. '@username')."
                    )

    if errors:
        print("❌ CODEOWNERS Coverage and Validation Check FAILED:")
        for err in errors:
            print(f"  - {err}")
        print("\nPlease fix the issues in .github/CODEOWNERS and push your changes.")
        sys.exit(1)

    print(
        "✅ All clinical boundary directories are correctly mapped to organizational teams in CODEOWNERS."
    )
    print(
        "✅ All mapped owners comply with organization group constraints (no individual user fallbacks)."
    )
    sys.exit(0)


if __name__ == "__main__":
    main()
