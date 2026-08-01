"""Unit tests for the GxP FMEA exemption ledger and vulnerability validation script.

This test module verifies the correctness of the scanning of inline bypass flags,
ledger schema validation, and vulnerability-to-exemption mapping logic.
"""

import json
from unittest.mock import patch

from scripts.validate_vulnerabilities import (
    extract_active_frontend_vulnerabilities,
    extract_active_vulnerabilities,
    load_and_validate_ledger,
    scan_for_inline_bypasses,
    scan_for_manifest_bypasses,
)


def test_scan_for_inline_bypasses_no_violations(tmp_path):
    """Verify that scan_for_inline_bypasses returns no violations when none exist.

    Requirements: PRD-SYS-001
    """
    (tmp_path / "apps").mkdir()
    (tmp_path / "packages").mkdir()

    clean_file = tmp_path / "apps" / "clean.py"
    clean_file.write_text("print('hello world')\n", encoding="utf-8")

    audit_file = tmp_path / "packages" / "audit.py"
    audit_file.write_text("uv run pip-audit\n", encoding="utf-8")

    with patch("scripts.validate_vulnerabilities.REPO_ROOT", str(tmp_path)):
        violations = scan_for_inline_bypasses()
        assert len(violations) == 0


def test_scan_for_inline_bypasses_with_violations(tmp_path):
    """Verify that scan_for_inline_bypasses identifies flag violations when adjacent to audit.

    Requirements: PRD-SYS-001
    """
    (tmp_path / "apps").mkdir()

    # File with long-form bypass flag and audit utility on same line
    dirty_file = tmp_path / "apps" / "dirty.yml"
    dirty_file.write_text(
        "run: uv run pip-audit --ignore-vuln CVE-12345\n", encoding="utf-8"
    )

    # File with short-form bypass flag and audit utility on same line
    short_dirty_file = tmp_path / "apps" / "short_dirty.py"
    short_dirty_file.write_text("pnpm audit -i CVE-1111\n", encoding="utf-8")

    # File with short flag but NO audit utility (should not be flagged)
    grep_file = tmp_path / "apps" / "grep.sh"
    grep_file.write_text("grep -i 'pattern' file.txt\n", encoding="utf-8")

    with patch("scripts.validate_vulnerabilities.REPO_ROOT", str(tmp_path)):
        violations = scan_for_inline_bypasses()
        assert len(violations) == 2

        paths = [v[0] for v in violations]
        assert str(dirty_file) in paths
        assert str(short_dirty_file) in paths
        assert str(grep_file) not in paths


def test_scan_for_manifest_bypasses(tmp_path):
    """Verify that scan_for_manifest_bypasses identifies non-empty pnpm.auditConfig overrides.

    Requirements: PRD-SYS-001
    """
    (tmp_path / "apps" / "app1").mkdir(parents=True)
    (tmp_path / "packages" / "pkg1").mkdir(parents=True)

    # package.json with empty/missing auditConfig
    pjson1 = tmp_path / "apps" / "app1" / "package.json"
    pjson1.write_text(
        json.dumps({"name": "app1", "dependencies": {}}), encoding="utf-8"
    )

    # package.json with non-empty auditConfig
    pjson2 = tmp_path / "packages" / "pkg1" / "package.json"
    pjson2.write_text(
        json.dumps(
            {"name": "pkg1", "pnpm": {"auditConfig": {"ignoreCves": ["CVE-2022-1234"]}}}
        ),
        encoding="utf-8",
    )

    with patch("scripts.validate_vulnerabilities.REPO_ROOT", str(tmp_path)):
        violations = scan_for_manifest_bypasses()
        assert len(violations) == 1
        assert str(pjson2) in violations[0][0]
        assert "pnpm.auditConfig" in violations[0][2]


def test_scan_exits_successfully_on_unreadable_files(tmp_path):
    """Verify that scanning silently ignores unreadable files and directories without crashing.

    Requirements: PRD-SYS-001
    """
    (tmp_path / "apps").mkdir()
    unreadable_file = tmp_path / "apps" / "locked.py"
    unreadable_file.write_text("pip-audit -i\n", encoding="utf-8")

    orig_open = open

    def mock_open(file, *args, **kwargs):
        if str(file) == str(unreadable_file):
            raise PermissionError("Permission denied (locked file mock)")
        return orig_open(file, *args, **kwargs)

    with patch("scripts.validate_vulnerabilities.REPO_ROOT", str(tmp_path)):
        with patch("builtins.open", mock_open):
            violations = scan_for_inline_bypasses()
            assert len(violations) == 0


def test_load_and_validate_ledger_not_found():
    """Verify load_and_validate_ledger handles non-existent ledger gracefully."""
    entries, errors = load_and_validate_ledger("/non/existent/ledger.json")
    assert len(entries) == 0
    assert "Ledger file not found" in errors[0]


def test_load_and_validate_ledger_invalid_json(tmp_path):
    """Verify validation fails with invalid JSON format."""
    bad_json = tmp_path / "bad.json"
    bad_json.write_text("{invalid", encoding="utf-8")

    entries, errors = load_and_validate_ledger(str(bad_json))
    assert len(entries) == 0
    assert "Failed to parse JSON ledger" in errors[0]


def test_load_and_validate_ledger_not_list(tmp_path):
    """Verify validation fails if the top-level element is not an array."""
    not_list = tmp_path / "not_list.json"
    not_list.write_text(json.dumps({"entry": "not a list"}), encoding="utf-8")

    entries, errors = load_and_validate_ledger(str(not_list))
    assert len(entries) == 0
    assert "top-level element must be a JSON array" in errors[0]


def test_load_and_validate_ledger_missing_vuln_id(tmp_path):
    """Verify validation fails when vulnerability_id is missing."""
    ledger = tmp_path / "ledger.json"
    ledger.write_text(
        json.dumps(
            [
                {
                    "severity": 3,
                    "occurrence": 2,
                    "detectability": 2,
                    "rpn": 12,
                    "justification": "Valid justification here",
                }
            ]
        ),
        encoding="utf-8",
    )

    entries, errors = load_and_validate_ledger(str(ledger))
    assert len(entries) == 0
    assert "missing a valid 'vulnerability_id'" in errors[0]


def test_load_and_validate_ledger_missing_fmea_fields(tmp_path):
    """Verify validation fails when FMEA parameters are missing."""
    ledger = tmp_path / "ledger.json"
    ledger.write_text(
        json.dumps(
            [
                {
                    "vulnerability_id": "PYSEC-1",
                    "severity": 3,
                    "occurrence": 2,
                    "justification": "Valid justification here",
                }
            ]
        ),
        encoding="utf-8",
    )

    entries, errors = load_and_validate_ledger(str(ledger))
    assert len(entries) == 0
    assert "missing FMEA parameters" in errors[0]


def test_load_and_validate_ledger_invalid_fmea_scores(tmp_path):
    """Verify validation fails with invalid or out-of-bounds FMEA scores."""
    ledger = tmp_path / "ledger.json"
    ledger.write_text(
        json.dumps(
            [
                {
                    "vulnerability_id": "PYSEC-1",
                    "severity": 6,  # Invalid (> 5)
                    "occurrence": 2,
                    "detectability": 2,
                    "rpn": 24,
                    "justification": "Valid justification here",
                }
            ]
        ),
        encoding="utf-8",
    )

    entries, errors = load_and_validate_ledger(str(ledger))
    assert len(entries) == 0
    assert "invalid FMEA scores" in errors[0]


def test_load_and_validate_ledger_incorrect_rpn(tmp_path):
    """Verify validation fails with incorrect pre-calculated RPN."""
    ledger = tmp_path / "ledger.json"
    ledger.write_text(
        json.dumps(
            [
                {
                    "vulnerability_id": "PYSEC-1",
                    "severity": 3,
                    "occurrence": 3,
                    "detectability": 2,
                    "rpn": 15,  # Incorrect (should be 18)
                    "justification": "Valid justification here",
                }
            ]
        ),
        encoding="utf-8",
    )

    entries, errors = load_and_validate_ledger(str(ledger))
    assert len(entries) == 0
    assert "invalid pre-calculated FMEA Risk Priority Number" in errors[0]


def test_load_and_validate_ledger_missing_justification(tmp_path):
    """Verify validation fails with a missing or too short justification."""
    ledger = tmp_path / "ledger.json"
    ledger.write_text(
        json.dumps(
            [
                {
                    "vulnerability_id": "PYSEC-1",
                    "severity": 3,
                    "occurrence": 3,
                    "detectability": 2,
                    "rpn": 18,
                    "justification": "Short",  # Too short
                }
            ]
        ),
        encoding="utf-8",
    )

    entries, errors = load_and_validate_ledger(str(ledger))
    assert len(entries) == 0
    assert "missing a robust GxP compliance justification" in errors[0]


def test_load_and_validate_ledger_valid(tmp_path):
    """Verify load_and_validate_ledger works perfectly with a valid schema."""
    ledger = tmp_path / "ledger.json"
    ledger.write_text(
        json.dumps(
            [
                {
                    "vulnerability_id": "PYSEC-1",
                    "severity": 3,
                    "occurrence": 2,
                    "detectability": 2,
                    "rpn": 12,
                    "justification": "The vulnerability is non-exploitable in local network environment",
                    "status": "active",
                }
            ]
        ),
        encoding="utf-8",
    )

    entries, errors = load_and_validate_ledger(str(ledger))
    assert len(errors) == 0
    assert len(entries) == 1
    assert entries[0]["vulnerability_id"] == "PYSEC-1"
    assert entries[0]["rpn"] == 12


def test_extract_active_vulnerabilities_invalid():
    """Verify extract_active_vulnerabilities handles empty or invalid output correctly."""
    vulns, err = extract_active_vulnerabilities("")
    assert len(vulns) == 0
    assert "No stdout returned" in err

    vulns, err = extract_active_vulnerabilities("{invalid_json")
    assert len(vulns) == 0
    assert "Failed to parse JSON" in err


def test_extract_active_vulnerabilities_valid():
    """Verify extract_active_vulnerabilities extracts all findings correctly."""
    sample_audit = {
        "dependencies": [
            {
                "name": "ecdsa",
                "version": "0.19.2",
                "vulns": [
                    {
                        "id": "PYSEC-123",
                        "description": "Timing attack vulnerabity",
                        "fix_versions": [],
                    }
                ],
            }
        ]
    }
    vulns, err = extract_active_vulnerabilities(json.dumps(sample_audit))
    assert not err
    assert len(vulns) == 1
    assert vulns[0]["vulnerability_id"] == "PYSEC-123"
    assert vulns[0]["package_name"] == "ecdsa"


def test_extract_active_frontend_vulnerabilities_invalid():
    """Verify extract_active_frontend_vulnerabilities handles empty or invalid output correctly."""
    vulns, err = extract_active_frontend_vulnerabilities("")
    assert len(vulns) == 0
    assert "No stdout returned" in err

    vulns, err = extract_active_frontend_vulnerabilities("{invalid_json")
    assert len(vulns) == 0
    assert "Failed to parse JSON" in err


def test_extract_active_frontend_vulnerabilities_valid():
    """Verify extract_active_frontend_vulnerabilities extracts all findings correctly."""
    sample_audit = {
        "advisories": {
            "1102341": {
                "id": 1102341,
                "title": "esbuild issue",
                "module_name": "esbuild",
                "github_advisory_id": "GHSA-67mh-4wv8-2f99",
                "patched_versions": ">=0.24.3",
                "findings": [{"version": "0.21.5"}],
            }
        }
    }
    vulns, err = extract_active_frontend_vulnerabilities(json.dumps(sample_audit))
    assert not err
    assert len(vulns) == 1
    assert vulns[0]["vulnerability_id"] == "GHSA-67mh-4wv8-2f99"
    assert vulns[0]["package_name"] == "esbuild"
    assert vulns[0]["version"] == "0.21.5"


def test_load_and_validate_ledger_frontend_invalid_justification(tmp_path):
    """Verify load_and_validate_ledger fails for frontend exceptions with justifications of exactly 10 characters or less."""
    ledger = tmp_path / "ledger.json"
    ledger.write_text(
        json.dumps(
            [
                {
                    "vulnerability_id": "GHSA-67mh-4wv8-2f99",
                    "package_name": "esbuild",
                    "severity": 2,
                    "occurrence": 2,
                    "detectability": 2,
                    "rpn": 8,
                    "justification": "1234567890",  # 10 chars (not exceeding 10)
                    "status": "active",
                }
            ]
        ),
        encoding="utf-8",
    )

    entries, errors = load_and_validate_ledger(str(ledger))
    assert len(entries) == 0
    assert "missing a robust GxP compliance justification" in errors[0]
    assert "exceeding 10" in errors[0]


def test_load_and_validate_ledger_frontend_invalid_rpn(tmp_path):
    """Verify load_and_validate_ledger fails for frontend exceptions with incorrect RPN."""
    ledger = tmp_path / "ledger.json"
    ledger.write_text(
        json.dumps(
            [
                {
                    "vulnerability_id": "GHSA-67mh-4wv8-2f99",
                    "package_name": "esbuild",
                    "severity": 2,
                    "occurrence": 2,
                    "detectability": 2,
                    "rpn": 10,  # Invalid (expected 8)
                    "justification": "Robust and long justification for GxP",
                    "status": "active",
                }
            ]
        ),
        encoding="utf-8",
    )

    entries, errors = load_and_validate_ledger(str(ledger))
    assert len(entries) == 0
    assert "invalid pre-calculated FMEA Risk Priority Number" in errors[0]
