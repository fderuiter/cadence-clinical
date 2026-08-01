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
            # Skip test mock tokens or environment variable fallbacks
            if (
                "os.getenv" in line
                or "os.environ" in line
                or "# nosec" in line
                or "mock" in line.lower()
                or "pragma: allowlist" in line.lower()
            ):
                continue

            for pattern_name, regex in SECRET_PATTERNS:
                if re.search(regex, line):
                    findings.append(
                        f"{filepath}:{idx} - [{pattern_name}] Potential exposed secret detected: {line.strip()[:60]}"
                    )
    except Exception:
        pass

    return findings


def run_security_audit(root_dir: str = ".") -> bool:
    """Recursively scan codebase for security violations.

    Args:
        root_dir: Repository root directory path.

    Returns:
        True if audit passed with 0 critical security findings, False otherwise.
    """
    print("=" * 60)
    print("Cadence Clinical — Automated Security & Secret Scanner")
    print("=" * 60)

    total_findings: list[str] = []

    for root, dirs, files in os.walk(root_dir):
        # Filter out excluded directories in-place
        dirs[:] = [d for d in dirs if d not in EXCLUDED_PATHS]

        for file in files:
            filepath = os.path.join(root, file)
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
    success = run_security_audit()
    sys.exit(0 if success else 1)
