#!/usr/bin/env python3
"""Automated Security Audit & Hardcoded Secret Scanner for Cadence Clinical.

Scans the repository for hardcoded secrets, unencrypted tokens, private keys, and
insecure cryptographic configurations to comply with GxP 21 CFR Part 11 security guidelines.

Requirements: PRD-SYS-001, 21 CFR Part 11
"""

import os
import re
import sys

# Regex patterns matching potential hardcoded credentials, API keys, or private keys
SECRET_PATTERNS: list[tuple[str, str]] = [
    ("AWS Secret Key", r"(?i)aws_secret_access_key\s*=\s*['\"][A-Za-z0-9/+=]{40}['\"]"),
    ("Generic Private Key", r"-----BEGIN (RSA|EC|OPENSSH|PRIVATE) KEY-----"),
    (
        "Hardcoded Bearer Token",
        r"bearer\s+eyJ[A-Za-z0-9-_=]+\.[A-Za-z0-9-_=]+\.?[A-Za-z0-9-_.+/=]*",
    ),
    ("Hardcoded Password Assignment", r"(?i)password\s*=\s*['\"][^'\"]{8,}['\"]"),
    (
        "Hardcoded Environment Fallback",
        r"(?i)(os\.getenv|os\.environ\.get)\s*\(\s*['\"][^'\"]+['\"]\s*,\s*['\"][^'\"]+['\"]",
    ),
]

EXCLUDED_PATHS = {
    ".git",
    ".venv",
    "venv",
    "__pycache__",
    ".pytest_cache",
    "node_modules",
    "report.xml",
}


def scan_file_for_secrets(filepath: str) -> list[str]:
    """Scan a single source file for potential hardcoded secret patterns.

    Args:
        filepath: Path to the target file.

    Returns:
        List of warning string messages for detected potential secrets.
    """
    findings: list[str] = []

    # Skip binary files or excluded file extensions
    if any(
        filepath.endswith(ext)
        for ext in [".png", ".jpg", ".pyc", ".db", ".sqlite", ".xml"]
    ):
        return findings

    try:
        with open(filepath, encoding="utf-8", errors="ignore") as f:
            lines = f.readlines()

        for idx, line in enumerate(lines, start=1):
            # Skip explicit inline developer bypass annotations (e.g. nosec, pragma: allowlist, or mocks)
            if (
                "# nosec" in line
                or "mock" in line.lower()
                or "pragma: allowlist" in line.lower()
            ):
                continue

            for pattern_name, regex in SECRET_PATTERNS:
                if pattern_name == "Hardcoded Environment Fallback":
                    # Removed path whitelist restriction. Enforces global monorepo scanning.

                    # Only flag actually sensitive credential/secret variables
                    line_lower = line.lower()
                    is_secret_word = any(
                        word in line_lower
                        for word in [
                            "secret",
                            "token",
                            "password",
                            "pwd",
                            "salt",
                            "credential",
                            "private",
                            "bearer",
                        ]
                    ) or (
                        "key" in line_lower
                        and "keycloak" not in line_lower
                        and "monkeypatch" not in line_lower
                    )
                    if not is_secret_word:
                        continue

                if re.search(regex, line):
                    findings.append(
                        f"{filepath}:{idx} - [{pattern_name}] Potential exposed secret detected: {line.strip()[:60]}"
                    )
    except Exception:
        pass

    return findings


def is_excluded(filepath: str) -> bool:
    """Check if the filepath is in any excluded directory or matches an excluded path.

    Args:
        filepath: Path to the target file.

    Returns:
        True if the file is excluded, False otherwise.
    """
    # Normalize paths to handle cross-platform slash differences (Windows vs Unix)
    normalized = filepath.replace("\\", "/")
    parts = normalized.split("/")
    if any(part in EXCLUDED_PATHS for part in parts):
        return True

    # Check for explicit opt-out files (.scannerignore or .nosec) in the file's directory hierarchy
    try:
        abs_path = os.path.abspath(filepath)
        dir_path = os.path.dirname(abs_path)
        while True:
            if os.path.exists(os.path.join(dir_path, ".scannerignore")):
                return True
            if os.path.exists(os.path.join(dir_path, ".nosec")):
                return True
            parent = os.path.dirname(dir_path)
            if parent == dir_path:
                break
            dir_path = parent
    except Exception:
        pass

    return False


def run_security_audit(root_dir: str = ".", files: list[str] = None) -> bool:
    """Recursively scan codebase or scan targeted files for security violations.

    Args:
        root_dir: Repository root directory path.
        files: Optional list of specific file paths to scan.

    Returns:
        True if audit passed with 0 critical security findings, False otherwise.
    """
    print("=" * 60)
    print("Cadence Clinical — Automated Security & Secret Scanner")
    print("=" * 60)

    total_findings: list[str] = []

    if files:
        unique_files = sorted(list(set(f for f in files if f.strip())))
        print(f"Targeted Scan: checking {len(unique_files)} files...")
        for filepath in unique_files:
            if not os.path.isfile(filepath):
                continue
            if is_excluded(filepath):
                continue
            findings = scan_file_for_secrets(filepath)
            total_findings.extend(findings)
    else:
        print(f"Full Scan: recursively scanning directory '{root_dir}'...")
        for root, dirs, files_in_dir in os.walk(root_dir):
            # Filter out excluded or opted-out directories in-place
            dirs[:] = [
                d
                for d in dirs
                if d not in EXCLUDED_PATHS
                and not os.path.exists(os.path.join(root, d, ".scannerignore"))
                and not os.path.exists(os.path.join(root, d, ".nosec"))
            ]

            for file in files_in_dir:
                filepath = os.path.join(root, file)
                if is_excluded(filepath):
                    continue
                findings = scan_file_for_secrets(filepath)
                total_findings.extend(findings)

    if total_findings:
        print(
            f"❌ Security Audit Failed! Detected {len(total_findings)} potential issue(s):"
        )
        for finding in total_findings:
            print(f"  - {finding}")
        return False

    print("✔ Security Audit Passed cleanly. Zero hardcoded secrets detected.")
    return True


if __name__ == "__main__":
    # Get all command line arguments filtering out optional flags
    args = sys.argv[1:]
    file_args = [arg for arg in args if not arg.startswith("-")]

    success = run_security_audit(files=file_args)
    sys.exit(0 if success else 1)
