#!/usr/bin/env python3
"""
Script: check-action-pinning.py
Description: Validates that all third-party GitHub Action usages in .github/workflows/
             are pinned to full 40-character commit SHAs with inline version comments.
"""

import os
import re
import sys
from pathlib import Path

# Matches 40-character hexadecimal commit SHAs
SHA_REGEX = re.compile(r'^[0-9a-fA-F]{40}$')

# Matches `uses:` lines in GitHub Actions workflow files
# e.g., `uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7`
# or `uses: ./.github/workflows/production-pipeline.yml`
USES_REGEX = re.compile(r'^\s*-\s*name:.*|^\s*uses:\s*(\S+)(?:\s+#\s*(.*))?')


def check_file(file_path: Path) -> list[str]:
    errors = []
    with open(file_path, "r", encoding="utf-8") as f:
        lines = f.readlines()

    for line_idx, raw_line in enumerate(lines, start=1):
        stripped = raw_line.strip()
        if not stripped or stripped.startswith("#"):
            continue

        if "uses:" not in raw_line:
            continue

        # Extract the value of uses:
        # e.g. "uses: actions/checkout@v7" or "uses: actions/checkout@3d3c42e... # v7"
        parts = raw_line.split("uses:", 1)
        if len(parts) < 2:
            continue

        action_expr = parts[1].strip()
        # Separate action ref from potential inline comment
        comment = ""
        if " #" in action_expr:
            ref_part, comment = action_expr.split(" #", 1)
            action_ref = ref_part.strip()
            comment = comment.strip()
        elif action_expr.startswith("#"):
            continue
        else:
            action_ref = action_expr.split("#")[0].strip()

        # Ignore local relative workflow calls and local action paths
        if action_ref.startswith("./") or action_ref.startswith("../") or action_ref.startswith("."):
            continue

        # Action ref format: owner/repo@ref or owner/repo/path@ref or docker://...
        if "@" not in action_ref:
            errors.append(
                f"{file_path}:{line_idx}: Action reference '{action_ref}' is missing '@<commit-sha>'."
            )
            continue

        action_name, ref = action_ref.split("@", 1)

        if not SHA_REGEX.match(ref):
            errors.append(
                f"{file_path}:{line_idx}: Action '{action_name}' is pinned to tag/branch '{ref}' instead of a 40-character commit SHA.\n"
                f"  Line: {raw_line.strip()}"
            )
        elif not comment:
            # Encouraged/required inline version comment for maintainability
            errors.append(
                f"{file_path}:{line_idx}: Action '{action_name}' uses commit SHA '{ref}' but lacks an inline version comment (e.g. '# v4').\n"
                f"  Line: {raw_line.strip()}"
            )

    return errors


def main() -> int:
    repo_root = Path(__file__).resolve().parent.parent
    workflows_dir = repo_root / ".github" / "workflows"

    if not workflows_dir.exists():
        print(f"Error: Workflows directory '{workflows_dir}' not found.")
        return 1

    workflow_files = sorted(
        list(workflows_dir.glob("*.yml")) + list(workflows_dir.glob("*.yaml"))
    )

    all_errors = []
    for wf in workflow_files:
        wf_errors = check_file(wf)
        all_errors.extend(wf_errors)

    if all_errors:
        print("=" * 80)
        print("ACTION PINNING LINTER ERRORS FOUND:")
        print("=" * 80)
        for err in all_errors:
            print(f"- {err}")
        print("\n" + "-" * 80)
        print("HOW TO FIX ACTION PINNING VULNERABILITIES:")
        print("1. All third-party GitHub Actions must reference a 40-character commit SHA.")
        print("2. Include an inline version comment (e.g. '# v4') for human readability.")
        print("3. To look up the full 40-character commit SHA for an action tag/release, run:")
        print("     git ls-remote https://github.com/<owner>/<repo>.git refs/tags/<tag>")
        print("   Or for annotated tags:")
        print("     git ls-remote https://github.com/<owner>/<repo>.git refs/tags/<tag>^{}")
        print("Example:")
        print("   uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7")
        print("-" * 80 + "\n")
        return 1

    print("Success: All third-party GitHub Actions are pinned to 40-character commit SHAs.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
