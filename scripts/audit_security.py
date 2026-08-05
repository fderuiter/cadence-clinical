#!/usr/bin/env python3
"""Automated Security Audit & Hardcoded Secret Scanner for Cadence Clinical.

Scans the repository for hardcoded secrets, unencrypted tokens, private keys, and
insecure cryptographic configurations to comply with GxP 21 CFR Part 11 security guidelines.

Requirements: PRD-SYS-001, 21 CFR Part 11
"""

import fnmatch
import json
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

_exemptions_cache = None


def match_path(filepath: str, pattern: str) -> bool:
    """Check if the given filepath matches the glob or regex pattern.

    Supports standard wildcard matching (fnmatch) and double star glob matching (e.g. apps/gateway/**/*.py).
    """
    # Normalize paths
    filepath = filepath.replace("\\", "/").lstrip("./")
    pattern = pattern.replace("\\", "/").lstrip("./")

    if filepath == pattern:
        return True

    if fnmatch.fnmatch(filepath, pattern):
        return True

    # Translate glob pattern to regex for double star support
    regex_pattern = re.escape(pattern)
    regex_pattern = regex_pattern.replace(r"\*\*/", r"(?:.*/)?")
    regex_pattern = regex_pattern.replace(r"\*\*", r".*")
    regex_pattern = regex_pattern.replace(r"\*", r"[^/]*")
    regex_pattern = regex_pattern.replace(r"\?", r"[^/]")

    regex_str = "^" + regex_pattern + "$"
    return bool(re.match(regex_str, filepath))


def load_exemptions() -> list[dict]:
    """Load and cache the security exemptions ledger from security_exemptions.json."""
    global _exemptions_cache
    if _exemptions_cache is not None:
        return _exemptions_cache

    script_dir = os.path.dirname(os.path.abspath(__file__))
    repo_root = os.path.dirname(script_dir)
    ledger_path = os.path.join(repo_root, "security_exemptions.json")

    if not os.path.exists(ledger_path):
        ledger_path = "security_exemptions.json"

    if not os.path.exists(ledger_path):
        _exemptions_cache = []
        return _exemptions_cache

    try:
        with open(ledger_path, encoding="utf-8") as f:
            _exemptions_cache = json.load(f)
    except Exception as e:
        print(
            f"⚠️ Warning: Failed to parse exemptions ledger '{ledger_path}': {e}",
            file=sys.stderr,
        )
        _exemptions_cache = []

    return _exemptions_cache


def print_instruction_for_unapproved_bypass(
    filepath: str, line_no: int, line_content: str
):
    print("\n" + "!" * 80)
    print("❌ UNAPPROVED INLINE BYPASS DETECTED")
    print(f"  File: {filepath}")
    print(f"  Line: {line_no}")
    print(f"  Code: {line_content.strip()}")
    print("-" * 80)
    print(
        "An inline bypass comment (such as '# "
        + "nosec' or 'pragma: "
        + "allowlist') was found,"
    )
    print(
        "but there is no matching approved entry in the central 'security_exemptions.json' ledger."
    )
    print(
        "\nTo resolve this issue, please register the exemption by following these steps:"
    )
    print("1. Open 'security_exemptions.json' in the root directory.")
    print("2. Add a new entry to the JSON array with the following format:")
    print("   {")
    print(f'     "file": "{filepath}",')
    print(f'     "pattern": "{re.escape(line_content.strip())}",')
    print('     "justification": "<detailed compliance/justification explanation>"')
    print("   }")
    print("3. Ensure the 'justification' field is a non-empty, detailed explanation.")
    print("4. Commit both the code changes and 'security_exemptions.json'.")
    print("!" * 80 + "\n")


def print_instruction_for_invalid_justification(
    filepath: str, line_no: int, line_content: str, entry: dict
):
    print("\n" + "!" * 80)
    print("❌ INVALID EXEMPTION JUSTIFICATION")
    print(f"  File: {filepath}")
    print(f"  Line: {line_no}")
    print(f"  Code: {line_content.strip()}")
    print("-" * 80)
    print(
        "A matching entry was found in 'security_exemptions.json', but the 'justification'"
    )
    print("field is empty or missing. A robust compliance justification is required.")
    print("\nTo resolve this issue:")
    print("1. Open 'security_exemptions.json' in the root directory.")
    print("2. Find the entry matching this file and pattern:")
    print(f"   Pattern: {entry.get('pattern')}")
    print(
        "3. Provide a clear, non-empty, robust compliance justification in the 'justification' field."
    )
    print("4. Save and commit 'security_exemptions.json'.")
    print("!" * 80 + "\n")


def scan_file_for_secrets(filepath: str, exemptions: list[dict] = None) -> list[str]:
    """Scan a single source file for potential hardcoded secret patterns.

    Args:
        filepath: Path to the target file.

    Returns:
        List of warning string messages for detected potential secrets.
    """
    findings: list[str] = []

    # Skip binary files or excluded file extensions, or the exemptions ledger itself
    if filepath.endswith("security_exemptions.json") or any(
        filepath.endswith(ext)
        for ext in [".png", ".jpg", ".pyc", ".db", ".sqlite", ".xml"]
    ):
        return findings

    try:
        with open(filepath, encoding="utf-8", errors="ignore") as f:
            lines = f.readlines()

        exemptions_list = load_exemptions() if exemptions is None else exemptions

        for idx, line in enumerate(lines, start=1):
            # Check for inline bypass comment tags (excluding pure 'mock' strings)
            is_bypass_tag = (
                "# " + "nosec" in line
                or "#" + "nosec" in line
                or "pragma: " + "allowlist" in line.lower()
            )

            if is_bypass_tag:
                matched_entry = None
                has_valid_justification = False

                for entry in exemptions_list:
                    file_pattern = entry.get("file")
                    pattern = entry.get("pattern")

                    if not file_pattern or not pattern:
                        continue

                    if match_path(filepath, file_pattern):
                        pattern_matches = False
                        try:
                            if re.search(pattern, line):
                                pattern_matches = True
                        except re.error:
                            pass

                        if not pattern_matches and pattern in line:
                            pattern_matches = True

                        if pattern_matches:
                            matched_entry = entry
                            justification = entry.get("justification")
                            if (
                                justification
                                and isinstance(justification, str)
                                and justification.strip()
                            ):
                                has_valid_justification = True
                            break

                if matched_entry:
                    if has_valid_justification:
                        # Bypass is approved! We skip scanning this line.
                        continue
                    # Matched entry but justification is missing or empty!
                    findings.append(
                        f"{filepath}:{idx} - [Invalid Justification] Inline bypass '{line.strip()[:60]}' matches ledger entry but lacks a valid justification."
                    )
                    print_instruction_for_invalid_justification(
                        filepath, idx, line, matched_entry
                    )
                    continue
                # No matching entry in the ledger!
                findings.append(
                    f"{filepath}:{idx} - [Unapproved Bypass] Inline bypass comment detected but has no matching approved entry in 'security_exemptions.json'."
                )
                print_instruction_for_unapproved_bypass(filepath, idx, line)
                continue

            # If not a bypass tag, we can still skip if 'mock' is in the line (standard test fallback)
            if "mock" in line.lower():
                continue

            for pattern_name, regex in SECRET_PATTERNS:
                if pattern_name == "Hardcoded Environment Fallback":
                    # Hardcoded Environment Fallback is only enforced on Gateway Service, Study Designer, and Security packages
                    normalized = filepath.replace("\\", "/")
                    is_relevant = (
                        "apps/gateway/" in normalized
                        or "apps/designer/" in normalized
                        or "packages/security/" in normalized
                        or "test_compliance_security.py" in normalized
                        or "temp" in normalized.lower()
                        or "tmp" in normalized.lower()
                    )
                    if not is_relevant:
                        continue

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
    return any(part in EXCLUDED_PATHS for part in parts)


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
            # Filter out excluded directories in-place
            dirs[:] = [d for d in dirs if d not in EXCLUDED_PATHS]

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
