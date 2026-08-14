#!/usr/bin/env python3
"""GxP-Aligned Vulnerability Validation & FMEA Exemption Ledger Guardrail.

This script enforces absolute compliance with FDA 21 CFR Part 11 and EU Annex 11
by validating dependency security vulnerabilities using FMEA calculations.
It scans pipeline configs for inline bypasses, ensures structured ledger compliance,
verifies that RPN scores are within safety limits (< 20), and outputs validation states.
"""

import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
from typing import Any

# Resolve the repository root directory dynamically to support both local container and CI runner environments.
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.dirname(SCRIPT_DIR)
if REPO_ROOT not in sys.path:
    sys.path.insert(0, REPO_ROOT)

# Enforce Python 3.14+ runtime before loading standard modules or packages
if sys.version_info < (3, 14):
    try:
        from scripts.runtime_guard import enforce_python_runtime

        enforce_python_runtime()
    except Exception:
        sys.stderr.write(
            f"[FATAL] Incompatible Python runtime {sys.version.split()[0]} ({sys.executable}).\n"
            "Cadence Clinical requires Python 3.14+.\n"
            "Please run: uv run python scripts/validate_vulnerabilities.py\n"
        )
        sys.exit(1)

from scripts.runtime_guard import enforce_python_runtime


def scan_for_inline_bypasses() -> list[tuple[str, int, str]]:
    """Scan the entire repository recursively for undocumented inline bypass flags.

    The scan identifies instances where short-form (`-i`) or long-form (`--ignore-vuln`,
    `--ignore-vulnerability`) bypass options are used adjacent to dependency audit utility
    execution in source, workflow, documentation, and shell/configuration files. Generated,
    dependency, database, and non-command data artifacts are excluded.

    Returns:
        list[tuple[str, int, str]]: A list of tuples containing (file_path, line_number, line_content)
            where inline bypass flags were found.

    Raises:
        None
    """
    audit_tool_pattern = re.compile(
        r"\b(pip-audit|pnpm\s+audit|npm\s+audit|yarn\s+audit)\b", re.IGNORECASE
    )
    flag_pattern = re.compile(
        r"(?:\s|^)-(i)\b|(?:\s|^)--(ignore-vuln|ignore-vulnerability)\b"
    )
    comment_pattern = re.compile(r"(?:\s+|^)#.*$")
    logical_boundary_pattern = re.compile(
        r"^-(?:\s*)(name|run|uses|task|step|job)\b", re.IGNORECASE
    )
    section_boundary_pattern = re.compile(
        r"^(jobs|steps|tasks|stages):\s*$", re.IGNORECASE
    )
    folded_block_pattern = re.compile(r">\s*[+-]?\s*$")
    shell_boundary_pattern = re.compile(r"&&|;|\|\||\|")
    violations: list[tuple[str, int, str]] = []

    excluded_dirs = {
        ".git",
        ".venv",
        "venv",
        "__pycache__",
        ".pytest_cache",
        ".mypy_cache",
        ".pnpm-store",
        ".ruff_cache",
        ".tox",
        "build",
        "coverage",
        "dist",
        "node_modules",
        "tests",
    }
    scannable_suffixes = {
        ".bash",
        ".cfg",
        ".conf",
        ".ini",
        ".js",
        ".jsx",
        ".md",
        ".py",
        ".sh",
        ".toml",
        ".ts",
        ".tsx",
        ".txt",
        ".vue",
        ".yaml",
        ".yml",
        ".zsh",
    }
    scannable_names = {"Dockerfile", "Makefile"}

    try:
        for root, dirs, files in os.walk(REPO_ROOT):
            # Filter out excluded directories in-place to prevent traversing them
            dirs[:] = [
                directory
                for directory in dirs
                if directory not in excluded_dirs and not directory.endswith("-data")
            ]

            for file in files:
                file_path = os.path.join(root, file)
                # Ignore this validation script itself to prevent false positives
                if "validate_vulnerabilities.py" in file:
                    continue
                _, suffix = os.path.splitext(file)
                if (
                    suffix.lower() not in scannable_suffixes
                    and file not in scannable_names
                ):
                    continue

                try:
                    is_yaml = file_path.endswith((".yml", ".yaml"))
                    with open(file_path, encoding="utf-8", errors="ignore") as f:
                        buffer: list[tuple[int, str]] = []
                        in_folded_block = False
                        block_indent = 0

                        for line_num, line in enumerate(f, 1):
                            # Strip trailing whitespace and comments
                            line_no_comment = (
                                comment_pattern.sub("", line) if "#" in line else line
                            )
                            stripped = line_no_comment.strip()

                            if stripped:
                                indent = len(line_no_comment) - len(
                                    line_no_comment.lstrip(" ")
                                )
                                if (
                                    is_yaml
                                    and in_folded_block
                                    and indent <= block_indent
                                ):
                                    in_folded_block = False

                            # 1. Check for logical boundary delimiters (only relevant or reset on boundaries)
                            is_boundary = False
                            if (
                                stripped.startswith("---")
                                or stripped.startswith("...")
                                or logical_boundary_pattern.match(stripped)
                                or section_boundary_pattern.match(stripped)
                            ):
                                is_boundary = True

                            if is_boundary:
                                buffer.clear()
                                in_folded_block = False
                                continue

                            # Check if starting a YAML folded block (e.g. run: >)
                            if is_yaml and folded_block_pattern.search(stripped):
                                in_folded_block = True
                                block_indent = len(line_no_comment) - len(
                                    line_no_comment.lstrip(" ")
                                )

                            # 2. Check for shell execution boundaries within the line.
                            # We split the line by standard shell boundaries: &&, ;, ||, |
                            segments = shell_boundary_pattern.split(line_no_comment)

                            for idx, seg in enumerate(segments):
                                seg_stripped = seg.strip()
                                if not seg_stripped:
                                    continue

                                # Add the segment to the sliding buffer
                                buffer.append((line_num, seg_stripped))
                                if len(buffer) > 3:
                                    buffer.pop(0)

                                # Check if BOTH audit tool and ignore flag are in the active buffer
                                has_audit = any(
                                    audit_tool_pattern.search(item[1])
                                    for item in buffer
                                )
                                has_flag = any(
                                    flag_pattern.search(item[1]) for item in buffer
                                )

                                if has_audit and has_flag:
                                    violations.append(
                                        (file_path, line_num, line.strip())
                                    )
                                    buffer.clear()
                                    break

                                # Check if this segment is a shell execution boundary.
                                # It's a boundary if:
                                # - It's not the last segment on the line.
                                # - It's the last segment on the line, but we are NOT in a YAML folded block,
                                #   AND the segment does NOT end with \.
                                is_last_seg = idx == len(segments) - 1
                                if not is_last_seg or (
                                    not in_folded_block
                                    and not seg_stripped.endswith("\\")
                                ):
                                    buffer.clear()

                except Exception:
                    # Catch and ignore file-read failures silently to prevent build crashes
                    pass
    except Exception:
        pass

    return violations


def scan_for_manifest_bypasses() -> list[tuple[str, int, str]]:
    """Scan all package.json files recursively for non-empty pnpm.auditConfig overrides.

    This identifies structured bypass configurations embedded within package manifests
    that attempt to exempt package dependencies.

    Returns:
        list[tuple[str, int, str]]: A list of tuples containing (file_path, line_number, error_message)
            where non-empty blocks were found.

    Raises:
        None
    """
    violations: list[tuple[str, int, str]] = []
    excluded_dirs = {
        ".git",
        ".venv",
        "venv",
        "__pycache__",
        ".pytest_cache",
        "node_modules",
        "tests",
    }

    try:
        for root, dirs, files in os.walk(REPO_ROOT):
            # Filter out excluded directories in-place
            dirs[:] = [d for d in dirs if d not in excluded_dirs]

            for file in files:
                if file == "package.json":
                    file_path = os.path.join(root, file)
                    try:
                        with open(file_path, encoding="utf-8") as f:
                            content = json.load(f)

                        pnpm_block = content.get("pnpm")
                        if isinstance(pnpm_block, dict):
                            audit_config = pnpm_block.get("auditConfig")
                            if audit_config is not None:
                                is_non_empty = False
                                if (
                                    isinstance(audit_config, dict)
                                    and audit_config
                                    or isinstance(audit_config, list)
                                    and audit_config
                                    or isinstance(audit_config, str)
                                    and audit_config.strip()
                                    or not isinstance(audit_config, (dict, list, str))
                                    and audit_config
                                ):
                                    is_non_empty = True

                                if is_non_empty:
                                    line_num = 1
                                    try:
                                        with open(
                                            file_path, encoding="utf-8"
                                        ) as f_lines:
                                            for idx, line in enumerate(f_lines, 1):
                                                if "auditConfig" in line:
                                                    line_num = idx
                                                    break
                                                if "pnpm" in line:
                                                    line_num = idx
                                    except Exception:
                                        pass

                                    violations.append(
                                        (
                                            file_path,
                                            line_num,
                                            f"pnpm.auditConfig override block: {json.dumps(audit_config)}",
                                        )
                                    )
                    except Exception:
                        # Silently ignore read or parse failures on locked/unreadable files
                        pass
    except Exception:
        pass

    return violations


def scan_for_config_bypasses() -> list[tuple[str, int, str]]:
    """Scan all .npmrc and .pnpmrc files recursively for audit override bypasses.

    This identifies configurations that attempt to skip, disable, or alter audit gates.

    Returns:
        list[tuple[str, int, str]]: A list of tuples containing (file_path, line_number, error_message)
            where bypasses were found.
    """
    violations: list[tuple[str, int, str]] = []
    excluded_dirs = {
        ".git",
        ".venv",
        "venv",
        "__pycache__",
        ".pytest_cache",
        "node_modules",
        "tests",
    }

    bypass_pattern = re.compile(
        r"audit\s*=\s*(?:false|skip)|audit-level\b|vulnerabilities\s*=\s*false",
        re.IGNORECASE,
    )

    try:
        for root, dirs, files in os.walk(REPO_ROOT):
            dirs[:] = [d for d in dirs if d not in excluded_dirs]

            for file in files:
                if file in {".npmrc", ".pnpmrc"}:
                    file_path = os.path.join(root, file)
                    try:
                        with open(file_path, encoding="utf-8", errors="ignore") as f:
                            for line_num, line in enumerate(f, 1):
                                if bypass_pattern.search(line):
                                    violations.append(
                                        (
                                            file_path,
                                            line_num,
                                            f"Audit configuration bypass detected: {line.strip()}",
                                        )
                                    )
                    except Exception:
                        pass
    except Exception:
        pass

    return violations


def load_and_validate_ledger(
    ledger_path: str,
) -> tuple[list[dict[str, Any]], list[str]]:
    """Load and perform strict schema and FMEA calculation validation on the ledger.

    Args:
        ledger_path: The absolute path to the vulnerability exclusions JSON ledger.

    Returns:
        A tuple containing (list of valid ledger entries, list of validation error strings).
    """
    if not os.path.exists(ledger_path):
        return [], [f"Ledger file not found at path: {ledger_path}"]

    try:
        with open(ledger_path, encoding="utf-8") as f:
            data = json.load(f)
    except Exception as e:
        return [], [f"Failed to parse JSON ledger from {ledger_path}: {e}"]

    if not isinstance(data, list):
        return [], ["Ledger format is invalid: top-level element must be a JSON array."]

    entries: list[dict[str, Any]] = []
    errors: list[str] = []

    for idx, entry in enumerate(data):
        if not isinstance(entry, dict):
            errors.append(f"Ledger entry #{idx} is not a valid JSON object.")
            continue

        vuln_id = entry.get("vulnerability_id")
        if not vuln_id or not isinstance(vuln_id, str):
            errors.append(f"Ledger entry #{idx} is missing a valid 'vulnerability_id'.")
            continue

        severity = entry.get("severity")
        occurrence = entry.get("occurrence")
        detectability = entry.get("detectability")
        rpn = entry.get("rpn")
        justification = entry.get("justification")
        status = entry.get("status", "active")

        # Check for missing values
        if (
            severity is None
            or occurrence is None
            or detectability is None
            or rpn is None
        ):
            errors.append(
                f"Vulnerability {vuln_id} has missing FMEA parameters. "
                "Each entry must include 'severity', 'occurrence', 'detectability', and 'rpn'."
            )
            continue

        # Check value types and bounds
        if (
            not isinstance(severity, int)
            or not isinstance(occurrence, int)
            or not isinstance(detectability, int)
            or not (1 <= severity <= 5)
            or not (1 <= occurrence <= 5)
            or not (1 <= detectability <= 5)
        ):
            errors.append(
                f"Vulnerability {vuln_id} has invalid FMEA scores. "
                "Severity, occurrence, and detectability must be integers between 1 and 5."
            )
            continue

        # Check correct product RPN calculation
        expected_rpn = severity * occurrence * detectability
        if rpn != expected_rpn:
            errors.append(
                f"Vulnerability {vuln_id} has an invalid pre-calculated FMEA Risk Priority Number (RPN). "
                f"Expected {expected_rpn} (Severity {severity} * Occurrence {occurrence} * Detectability {detectability}), "
                f"but found {rpn}."
            )
            continue

        if rpn >= 20:
            errors.append(
                f"Vulnerability {vuln_id} has a high FMEA Risk Priority Number (RPN) of {rpn} >= 20, which violates existing validation thresholds."
            )
            continue

        # Justification check
        is_frontend = vuln_id.startswith("GHSA-")
        min_len = 11 if is_frontend else 10
        if (
            not justification
            or not isinstance(justification, str)
            or len(justification.strip()) < min_len
        ):
            errors.append(
                f"Vulnerability {vuln_id} is missing a robust GxP compliance justification "
                f"(must be a non-empty string of {'exceeding 10' if is_frontend else 'at least 10'} characters)."
            )
            continue

        entry["status"] = status
        entries.append(entry)

    return entries, errors


def execute_pip_audit() -> tuple[str, str, int]:
    """Execute pip-audit in JSON format and return stdout, stderr, and exit code.

    Returns:
        A tuple of (stdout, stderr, return_code).
    """
    # Create a temporary file to store the production-only requirements manifest.
    # Set delete=False so that uv export can write to the file and we can safely read it,
    # then manually delete it in the finally block.
    with tempfile.NamedTemporaryFile(suffix=".txt", delete=False) as temp_requirements:
        temp_file_path = temp_requirements.name

    try:
        # 1. Export production-only dependencies to the temporary manifest
        export_cmd = [
            "uv",
            "export",
            "--format",
            "requirements.txt",
            "--all-packages",
            "--no-dev",
            "--no-emit-project",
            "--no-hashes",
            "--no-emit-local",
            "--no-emit-workspace",
            "-o",
            temp_file_path,
        ]
        export_res = subprocess.run(
            export_cmd,
            cwd=REPO_ROOT,
            capture_output=True,
            text=True,
            check=False,
        )
        if export_res.returncode != 0:
            return (
                "",
                f"Failed to export production requirements: {export_res.stderr.strip()}",
                export_res.returncode,
            )

        # 2. Audit the exported requirements using pip-audit
        audit_cmd = [
            "uv",
            "run",
            "pip-audit",
            "-r",
            temp_file_path,
            "--format",
            "json",
        ]
        res = subprocess.run(
            audit_cmd,
            cwd=REPO_ROOT,
            capture_output=True,
            text=True,
            check=False,
        )
        return res.stdout.strip(), res.stderr.strip(), res.returncode
    except Exception as e:
        return "", str(e), -1
    finally:
        # Securely delete the temporary requirements manifest file immediately after audit completion
        try:
            if os.path.exists(temp_file_path):
                os.unlink(temp_file_path)
        except Exception:
            pass


def extract_active_vulnerabilities(audit_json: str) -> tuple[list[dict[str, Any]], str]:
    """Parse pip-audit output and extract individual vulnerability findings.

    Args:
        audit_json: Raw JSON stdout string from pip-audit execution.

    Returns:
        A tuple of (list of vulnerability dicts, error message string).
    """
    if not audit_json:
        return [], "No stdout returned from pip-audit."

    try:
        data = json.loads(audit_json)
    except Exception as e:
        return [], f"Failed to parse JSON output from pip-audit: {e}"

    vulns_list: list[dict[str, Any]] = []
    dependencies = data.get("dependencies", [])
    for dep in dependencies:
        dep_name = dep.get("name")
        dep_version = dep.get("version")
        vulns = dep.get("vulns", [])
        for vuln in vulns:
            v_id = vuln.get("id")
            if v_id:
                vulns_list.append(
                    {
                        "vulnerability_id": v_id,
                        "package_name": dep_name,
                        "version": dep_version,
                        "description": vuln.get("description", ""),
                        "fix_versions": vuln.get("fix_versions", []),
                    }
                )
    return vulns_list, ""


def execute_pnpm_audit() -> tuple[str, str, int]:
    """Execute pnpm audit in JSON format and return stdout, stderr, and exit code.

    Returns:
        A tuple of (stdout, stderr, return_code).
    """
    cmd = ["pnpm", "audit", "--json", "--prod"]
    if not shutil.which("pnpm"):
        cmd = ["npx", "-y", "pnpm", "audit", "--json", "--prod"]
    try:
        res = subprocess.run(
            cmd,
            capture_output=True,
            text=True,
            check=False,
        )
        return res.stdout.strip(), res.stderr.strip(), res.returncode
    except Exception as e:
        return "", str(e), -1


def extract_active_frontend_vulnerabilities(
    audit_json: str,
) -> tuple[list[dict[str, Any]], str]:
    """Parse pnpm audit output and extract individual vulnerability findings.

    Args:
        audit_json: Raw JSON stdout string from pnpm audit execution.

    Returns:
        A tuple of (list of vulnerability dicts, error message string).
    """
    if not audit_json:
        return [], "No stdout returned from pnpm audit."

    try:
        data = json.loads(audit_json)
    except Exception as e:
        return [], f"Failed to parse JSON output from pnpm audit: {e}"

    vulns_list: list[dict[str, Any]] = []

    # Detect legacy schema: "advisories" key exists
    if "advisories" in data:
        advisories = data.get("advisories", {})
        for adv_id, adv in advisories.items():
            v_id = adv.get("github_advisory_id") or adv.get("id") or str(adv_id)
            if not isinstance(v_id, str):
                v_id = str(v_id)
            module_name = adv.get("module_name")

            findings = adv.get("findings", [])
            version = "unknown"
            if findings and isinstance(findings, list):
                version = findings[0].get("version", "unknown")

            vulns_list.append(
                {
                    "vulnerability_id": v_id,
                    "package_name": module_name,
                    "version": version,
                    "description": adv.get("title", ""),
                    "fix_versions": adv.get("patched_versions", ""),
                }
            )

    # Detect modern schema: "vulnerabilities" key exists
    elif "vulnerabilities" in data:
        vulnerabilities = data.get("vulnerabilities", {})
        for pkg_name, vuln in vulnerabilities.items():
            via_list = vuln.get("via", [])
            if not isinstance(via_list, list):
                via_list = [via_list]

            # If via is empty, use the vuln itself as the source
            if not via_list:
                via_list = [vuln]

            for via in via_list:
                if isinstance(via, str):
                    # In npm/pnpm audit, string entries in "via" indicate a dependency relationship.
                    # The actual advisory is defined under that dependency's package name.
                    # To avoid duplicate or ID-less entries, we skip string entries.
                    continue

                if isinstance(via, dict):
                    module_name = (
                        via.get("name")
                        or via.get("dependency")
                        or vuln.get("name")
                        or vuln.get("dependency")
                        or pkg_name
                    )

                    # Extract vulnerability ID (GHSA, CVE, or source ID)
                    v_id = None
                    url = via.get("url") or vuln.get("url")
                    if url and isinstance(url, str):
                        match = re.search(r"(GHSA-[a-zA-Z0-9-]+)", url, re.IGNORECASE)
                        if match:
                            v_id = match.group(1).upper()

                    if not v_id:
                        cves = via.get("cves") or vuln.get("cves") or []
                        if cves and isinstance(cves, list):
                            v_id = cves[0]

                    if not v_id:
                        v_id = via.get("github_advisory_id") or vuln.get(
                            "github_advisory_id"
                        )

                    if not v_id:
                        source = via.get("source") or vuln.get("source")
                        if source:
                            source_str = str(source)
                            if "GHSA-" in source_str or "CVE-" in source_str:
                                v_id = source_str
                            else:
                                v_id = source_str

                    if not v_id:
                        v_id = "unknown"

                    # Extract installed version
                    version = via.get("version") or vuln.get("version")
                    if not version:
                        findings = via.get("findings") or vuln.get("findings") or []
                        if findings and isinstance(findings, list):
                            version = findings[0].get("version")
                    if not version:
                        version = "unknown"

                    # Extract/calculate patched versions
                    vuln_range = via.get("range") or vuln.get("range") or "unknown"
                    fix_versions = (
                        via.get("patched_versions")
                        or via.get("fix")
                        or vuln.get("patched_versions")
                    )
                    if not fix_versions and vuln_range and vuln_range != "unknown":
                        # Helper logic to extract patched version from range
                        parts = [p.strip() for p in vuln_range.split("||")]
                        fixed_parts = []
                        for part in parts:
                            match_less_eq = re.search(r"<=([0-9.]+)", part)
                            if match_less_eq:
                                fixed_parts.append(f">{match_less_eq.group(1)}")
                                continue
                            match_less = re.search(r"<([0-9.]+)", part)
                            if match_less:
                                fixed_parts.append(f">={match_less.group(1)}")
                                continue
                        if fixed_parts:
                            fix_versions = " || ".join(fixed_parts)
                        else:
                            fix_versions = vuln_range
                    if not fix_versions:
                        fix_versions = "unknown"

                    description = (
                        via.get("title")
                        or via.get("description")
                        or vuln.get("title")
                        or vuln.get("description")
                        or ""
                    )

                    vulns_list.append(
                        {
                            "vulnerability_id": v_id,
                            "package_name": module_name,
                            "version": version,
                            "description": description,
                            "fix_versions": fix_versions,
                        }
                    )

    return vulns_list, ""


def main() -> None:
    """Core verification orchestrator."""
    print("--- Starting GxP FMEA-Aligned Vulnerability Exemption Ledger Validation ---")

    # Step 0: Check command-line arguments for attempts to bypass
    for arg in sys.argv[1:]:
        if arg in [
            "-i",
            "--ignore-vuln",
            "--ignore-vulnerability",
            "--skip-audit",
            "--bypass",
        ]:
            print(
                f"\n[!] GxP Compliance Failure: Command-line bypass flag detected: {arg}"
            )
            sys.exit(1)

    ledger_path = os.path.join(
        REPO_ROOT, "docs/SDLC/vulnerability_exclusions_ledger.json"
    )
    summary_path = "/tmp/vulnerability_summary.json"  # nosec B108

    # Step 1: Scan for inline bypass configurations
    print("Scanning workflow files and scripts for inline bypasses...")
    inline_violations = scan_for_inline_bypasses()
    if inline_violations:
        print("\n[!] GxP Compliance Failure: Inline vulnerability bypasses detected:")
        for file_path, line_num, line_content in inline_violations:
            print(f"    - {file_path}:{line_num} -> {line_content}")

    # Step 1b: Scan for package.json manifest bypass configurations
    print("Scanning package.json files for pnpm.auditConfig overrides...")
    manifest_violations = scan_for_manifest_bypasses()
    if manifest_violations:
        print(
            "\n[!] GxP Compliance Failure: Manifest (package.json) vulnerability bypasses detected:"
        )
        for file_path, line_num, line_content in manifest_violations:
            print(f"    - {file_path}:{line_num} -> {line_content}")

    # Step 1c: Scan for .npmrc and .pnpmrc bypass configurations
    print("Scanning .npmrc and .pnpmrc files for audit bypass configurations...")
    config_violations = scan_for_config_bypasses()
    if config_violations:
        print(
            "\n[!] GxP Compliance Failure: Configuration (.npmrc/.pnpmrc) vulnerability bypasses detected:"
        )
        for file_path, line_num, line_content in config_violations:
            print(f"    - {file_path}:{line_num} -> {line_content}")

    # Step 2: Validate ledger entries
    print("Validating compliance ledger...")
    ledger_entries, ledger_errors = load_and_validate_ledger(ledger_path)
    if ledger_errors:
        print("\n[!] GxP Compliance Failure: Ledger validation failed with errors:")
        for err in ledger_errors:
            print(f"    - {err}")

    # Step 3: Execute vulnerability audit
    print("Running automated dependency vulnerability audit (pip-audit)...")
    stdout, stderr, code = execute_pip_audit()

    active_vulnerabilities: list[dict[str, Any]] = []
    audit_error = ""

    if code == 0:
        print(
            "Dependency audit completed successfully with zero vulnerability findings."
        )
    elif code == 1:
        print("Dependency audit completed. Active vulnerabilities found.")
        active_vulnerabilities, audit_error = extract_active_vulnerabilities(stdout)
        if audit_error:
            print(f"[!] Error parsing audit results: {audit_error}")
    else:
        print(f"[!] Warning: pip-audit exited with unexpected error code {code}.")
        print(f"    Stderr: {stderr}")
        audit_error = f"pip-audit failed to execute successfully: {stderr}"

    # Step 3b: Execute frontend vulnerability audit
    print("Running automated frontend dependency vulnerability audit (pnpm audit)...")
    p_stdout, p_stderr, p_code = execute_pnpm_audit()

    active_frontend_vulnerabilities: list[dict[str, Any]] = []
    frontend_audit_error = ""

    if p_code == 0:
        print(
            "Frontend dependency audit completed successfully with zero vulnerability findings."
        )
    elif p_code == 1:
        print("Frontend dependency audit completed. Active vulnerabilities found.")
        active_frontend_vulnerabilities, frontend_audit_error = (
            extract_active_frontend_vulnerabilities(p_stdout)
        )
        if frontend_audit_error:
            print(f"[!] Error parsing frontend audit results: {frontend_audit_error}")
    else:
        print(f"[!] Warning: pnpm audit exited with unexpected error code {p_code}.")
        print(f"    Stderr: {p_stderr}")
        frontend_audit_error = f"pnpm audit failed to execute successfully: {p_stderr}"

    # Step 4: Map active vulnerabilities against validated ledger entries
    print("Mapping active vulnerabilities against the GxP FMEA exemption ledger...")
    processed_vulns: list[dict[str, Any]] = []
    has_unapproved_vulns = False

    ledger_map = {}
    for entry in ledger_entries:
        v_id = entry.get("vulnerability_id")
        p_name = entry.get("package_name", "")
        ledger_map[(v_id, p_name)] = entry

    all_vulnerabilities = [(v, "Python") for v in active_vulnerabilities] + [
        (v, "Frontend") for v in active_frontend_vulnerabilities
    ]

    for vuln, source_type in all_vulnerabilities:
        v_id = vuln["vulnerability_id"]
        pkg = vuln["package_name"]
        ver = vuln["version"]

        if (v_id, pkg) in ledger_map:
            entry = ledger_map[(v_id, pkg)]
            rpn = entry["rpn"]
            justification = entry["justification"]
            status = entry.get("status", "active")

            if status != "active":
                print(
                    f"[❌] {source_type} vulnerability {v_id} matches ledger entry but its status is '{status}' (not active)."
                )
                vuln_status = "Blocked"
                has_unapproved_vulns = True
            elif rpn < 20:
                print(
                    f"[✅] {source_type} vulnerability {v_id} ({pkg}@{ver}) matches validated low-risk exemption ledger entry with RPN {rpn} < 20."
                )
                vuln_status = "Approved"
            else:
                print(
                    f"[❌] {source_type} vulnerability {v_id} ({pkg}@{ver}) yields a high FMEA Risk Priority Number (RPN) of {rpn} >= 20. Blocked from automatic progression."
                )
                vuln_status = "Blocked"
                has_unapproved_vulns = True

            processed_vulns.append(
                {
                    "vulnerability_id": v_id,
                    "package_name": pkg,
                    "version": ver,
                    "rpn": rpn,
                    "status": vuln_status,
                    "justification": justification,
                }
            )
        else:
            matching_ids = [
                e for e in ledger_entries if e.get("vulnerability_id") == v_id
            ]
            if matching_ids:
                exempted_packages = ", ".join(
                    repr(e.get("package_name", "")) for e in matching_ids
                )
                print(
                    f"[❌] {source_type} vulnerability {v_id} found in ledger, but exemption only applies to package(s): {exempted_packages}. "
                    f"It does not apply to active package: '{pkg}'. Blocked from automatic progression."
                )
            else:
                print(
                    f"[❌] {source_type} vulnerability {v_id} ({pkg}@{ver}) has no corresponding entry in the compliance ledger."
                )
            has_unapproved_vulns = True
            processed_vulns.append(
                {
                    "vulnerability_id": v_id,
                    "package_name": pkg,
                    "version": ver,
                    "rpn": "N/A",
                    "status": "Blocked",
                    "justification": f"Undocumented vulnerability bypass. No FMEA assessment exists for {source_type}.",
                }
            )

    # Determine overall pass/fail state
    all_passed = (
        not inline_violations
        and not manifest_violations
        and not config_violations
        and not ledger_errors
        and not has_unapproved_vulns
        and not audit_error
        and not frontend_audit_error
    )

    # Step 5: Save execution state summary for PR comment generator
    print(f"Writing GxP security compliance summary to {summary_path}...")
    summary_data = {
        "all_passed": all_passed,
        "vulnerabilities": processed_vulns,
        "inline_violations": inline_violations
        + manifest_violations
        + config_violations,
        "ledger_errors": ledger_errors,
    }

    try:
        with open(summary_path, "w", encoding="utf-8") as f:
            json.dump(summary_data, f, indent=2)
    except Exception as e:
        print(f"[!] Warning: Failed to save compliance summary file: {e}")

    # Step 6: Print validation summary report and exit
    print("\n--- GxP Security Compliance Validation Report ---")
    print(f"Inline bypass violations: {len(inline_violations)}")
    print(f"Manifest bypass violations: {len(manifest_violations)}")
    print(f"Config bypass violations: {len(config_violations)}")
    print(f"Ledger schema/FMEA errors: {len(ledger_errors)}")
    print(f"Active Python vulnerabilities: {len(active_vulnerabilities)}")
    print(f"Active Frontend vulnerabilities: {len(active_frontend_vulnerabilities)}")
    print(
        f"Blocked vulnerability exclusions: {sum(1 for v in processed_vulns if v['status'] == 'Blocked')}"
    )
    print(f"Overall GxP Compliance Gate: {'PASSED' if all_passed else 'FAILED'}")

    if not all_passed:
        sys.exit(1)


if __name__ == "__main__":
    main()
