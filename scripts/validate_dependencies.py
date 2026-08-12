#!/usr/bin/env python3
"""Dependency analyzer for forbidden public-key/asymmetric cryptographic packages in package.json files.

Enforces compliance with GxP and 21 CFR Part 11 requirements by ensuring
all asymmetric verification occurs on secure backends rather than client-side.

Requirements: PRD-SYS-001
"""

import json
import os
import sys
from pathlib import Path

# Absolute paths
ROOT_DIR = Path(__file__).resolve().parent.parent

# List of unauthorized asymmetric/public-key cryptographic npm packages
FORBIDDEN_PACKAGES = {
    "node-forge",
    "elliptic",
    "jose",
    "jsrsasign",
    "ursa",
    "keypair",
    "node-rsa",
    "openpgp",
    "forge",
    "sshpk",
    "tweetnacl",
    "@noble/curves",
    "@noble/secp256k1",
    "@noble/ed25519",
    "secp256k1",
}


def check_package_json(file_path: Path) -> list[str]:
    """Inspects a single package.json file's dependencies for unauthorized packages.

    Args:
        file_path (Path): Absolute path to the package.json file.

    Returns:
        list[str]: A list of violations found in the file.
    """
    violations = []
    try:
        with open(file_path, "r", encoding="utf-8") as f:
            data = json.load(f)

        # Inspect all dependency blocks
        for block in ["dependencies", "devDependencies", "peerDependencies"]:
            deps = data.get(block, {})
            if isinstance(deps, dict):
                for dep_name in deps.keys():
                    if dep_name.lower() in FORBIDDEN_PACKAGES:
                        violations.append(
                            f"Unauthorized asymmetric cryptographic package '{dep_name}' "
                            f"detected in '{block}'."
                        )
    except Exception as e:
        violations.append(f"Failed to read or parse package.json: {e}")

    return violations


def main():
    """Main entry point for the dependency analyzer script."""
    print("--- Starting Workspace Dependency Analyzer ---")
    violations_found = {}
    total_files_checked = 0

    # Walk repository to find all package.json files (ignoring standard exclusion paths)
    exclude_dirs = {".venv", "venv", "node_modules", ".git", "dist", "build", ".pytest_cache", ".ruff_cache"}

    for root, dirs, files in os.walk(ROOT_DIR):
        # Filter directories in-place to optimize traversal
        dirs[:] = [d for d in dirs if d not in exclude_dirs]

        for file in files:
            if file == "package.json":
                file_path = Path(root) / file
                total_files_checked += 1
                violations = check_package_json(file_path)
                if violations:
                    relative_path = str(file_path.relative_to(ROOT_DIR))
                    violations_found[relative_path] = violations

    if violations_found:
        print("\n[ERROR] Forbidden asymmetric/public-key cryptographic package(s) detected in package.json files!")
        for file, errs in violations_found.items():
            print(f"\nIn file: {file}")
            for err in errs:
                print(f"  - {err}")
        print("\nCI build block: asymmetric operations must be isolated inside native Python backends.")
        sys.exit(1)

    print(f"\n[SUCCESS] No forbidden asymmetric cryptographic dependencies found across {total_files_checked} package.json files.")
    sys.exit(0)


if __name__ == "__main__":
    main()
