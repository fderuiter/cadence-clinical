"""Unit tests for the GxP FMEA exemption ledger and vulnerability validation script.

This test module verifies the correctness of the scanning of inline bypass flags,
ledger schema validation, and vulnerability-to-exemption mapping logic.
Additionally validated for full GxP compliance and accessibility checks.
"""

import json
from unittest.mock import patch

from scripts.validate_vulnerabilities import (
    extract_active_frontend_vulnerabilities,
    extract_active_vulnerabilities,
    load_and_validate_ledger,
    scan_for_config_bypasses,
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


def test_load_and_validate_ledger_rpn_threshold(tmp_path):
    """Verify that load_and_validate_ledger fails when RPN is >= 20."""
    ledger = tmp_path / "ledger.json"
    ledger.write_text(
        json.dumps(
            [
                {
                    "vulnerability_id": "PYSEC-2026-9999",
                    "package_name": "ecdsa",
                    "severity": 5,
                    "occurrence": 4,
                    "detectability": 1,
                    "rpn": 20,
                    "justification": "Valid justification here for GxP purposes",
                    "status": "active",
                }
            ]
        ),
        encoding="utf-8",
    )
    entries, errors = load_and_validate_ledger(str(ledger))
    assert len(entries) == 0
    assert len(errors) == 1
    assert "violates existing validation thresholds" in errors[0]


def test_load_and_validate_ledger_multiple_entries_same_id(tmp_path):
    """Verify that multiple ledger entries with the same vulnerability ID but different package names are parsed successfully."""
    ledger = tmp_path / "ledger.json"
    ledger.write_text(
        json.dumps(
            [
                {
                    "vulnerability_id": "CVE-2026-SAME",
                    "package_name": "pkg-a",
                    "severity": 3,
                    "occurrence": 2,
                    "detectability": 2,
                    "rpn": 12,
                    "justification": "Valid justification for package A",
                    "status": "active",
                },
                {
                    "vulnerability_id": "CVE-2026-SAME",
                    "package_name": "pkg-b",
                    "severity": 2,
                    "occurrence": 2,
                    "detectability": 2,
                    "rpn": 8,
                    "justification": "Valid justification for package B",
                    "status": "active",
                },
            ]
        ),
        encoding="utf-8",
    )
    entries, errors = load_and_validate_ledger(str(ledger))
    assert len(errors) == 0
    assert len(entries) == 2
    assert entries[0]["package_name"] == "pkg-a"
    assert entries[1]["package_name"] == "pkg-b"


@patch("scripts.validate_vulnerabilities.load_and_validate_ledger")
@patch("scripts.validate_vulnerabilities.scan_for_inline_bypasses", return_value=[])
@patch("scripts.validate_vulnerabilities.scan_for_manifest_bypasses", return_value=[])
@patch("scripts.validate_vulnerabilities.scan_for_config_bypasses", return_value=[])
@patch("scripts.validate_vulnerabilities.execute_pip_audit")
@patch("scripts.validate_vulnerabilities.execute_pnpm_audit")
@patch("scripts.validate_vulnerabilities.extract_active_vulnerabilities")
@patch("scripts.validate_vulnerabilities.extract_active_frontend_vulnerabilities")
@patch("scripts.validate_vulnerabilities.sys.exit")
def test_validate_vulnerabilities_compound_matching(
    mock_exit,
    mock_extract_frontend,
    mock_extract_python,
    mock_execute_pnpm,
    mock_execute_pip,
    _mock_config_scan,
    _mock_manifest_scan,
    _mock_inline_scan,
    mock_load_ledger,
):
    """Verify that compliance run fails if vuln ID matches but package name mismatches, and passes if both match."""
    from scripts.validate_vulnerabilities import main

    # Setup mock ledger with CVE-2026-1111 exempted for package 'ecdsa'
    mock_load_ledger.return_value = (
        [
            {
                "vulnerability_id": "CVE-2026-1111",
                "package_name": "ecdsa",
                "severity": 3,
                "occurrence": 2,
                "detectability": 2,
                "rpn": 12,
                "justification": "Approved exemption justification here",
                "status": "active",
            }
        ],
        [],
    )

    # Setup active Python vulnerability: CVE-2026-1111 but for package 'django' (mismatch)
    mock_execute_pip.return_value = ("stdout", "stderr", 1)
    mock_extract_python.return_value = (
        [
            {
                "vulnerability_id": "CVE-2026-1111",
                "package_name": "django",
                "version": "4.2.0",
            }
        ],
        "",
    )

    # Frontend returns no vulnerabilities
    mock_execute_pnpm.return_value = ("stdout", "stderr", 0)
    mock_extract_frontend.return_value = ([], "")

    # Run main and catch exit/errors
    main()

    # It must exit with code 1 (failure) because package name django does not match ecdsa
    mock_exit.assert_called_once_with(1)
    mock_exit.reset_mock()

    # Now let's test a PASS scenario where the package name matches exactly (ecdsa)
    mock_extract_python.return_value = (
        [
            {
                "vulnerability_id": "CVE-2026-1111",
                "package_name": "ecdsa",
                "version": "0.19.2",
            }
        ],
        "",
    )

    main()

    # Since it matches, it should pass (sys.exit(1) is NOT called)
    mock_exit.assert_not_called()


@patch("scripts.validate_vulnerabilities.load_and_validate_ledger")
@patch("scripts.validate_vulnerabilities.scan_for_inline_bypasses", return_value=[])
@patch("scripts.validate_vulnerabilities.scan_for_manifest_bypasses", return_value=[])
@patch("scripts.validate_vulnerabilities.scan_for_config_bypasses", return_value=[])
@patch("scripts.validate_vulnerabilities.execute_pip_audit")
@patch("scripts.validate_vulnerabilities.execute_pnpm_audit")
@patch("scripts.validate_vulnerabilities.extract_active_vulnerabilities")
@patch("scripts.validate_vulnerabilities.extract_active_frontend_vulnerabilities")
@patch("scripts.validate_vulnerabilities.sys.exit")
def test_validate_vulnerabilities_multiple_identical_vuln_ids(
    mock_exit,
    mock_extract_frontend,
    mock_extract_python,
    mock_execute_pnpm,
    mock_execute_pip,
    _mock_config_scan,
    _mock_manifest_scan,
    _mock_inline_scan,
    mock_load_ledger,
):
    """Verify that multiple entries with identical vuln IDs but different packages are validated independently."""
    from scripts.validate_vulnerabilities import main

    # Setup mock ledger with CVE-2026-2222 for both 'ecdsa' and 'paramiko'
    mock_load_ledger.return_value = (
        [
            {
                "vulnerability_id": "CVE-2026-2222",
                "package_name": "ecdsa",
                "severity": 3,
                "occurrence": 2,
                "detectability": 2,
                "rpn": 12,
                "justification": "Justification for ecdsa",
                "status": "active",
            },
            {
                "vulnerability_id": "CVE-2026-2222",
                "package_name": "paramiko",
                "severity": 2,
                "occurrence": 2,
                "detectability": 2,
                "rpn": 8,
                "justification": "Justification for paramiko",
                "status": "active",
            },
        ],
        [],
    )

    # Active vulnerabilities: CVE-2026-2222 on both ecdsa and paramiko
    mock_execute_pip.return_value = ("stdout", "stderr", 1)
    mock_extract_python.return_value = (
        [
            {
                "vulnerability_id": "CVE-2026-2222",
                "package_name": "ecdsa",
                "version": "0.19.2",
            },
            {
                "vulnerability_id": "CVE-2026-2222",
                "package_name": "paramiko",
                "version": "3.4.0",
            },
        ],
        "",
    )

    mock_execute_pnpm.return_value = ("stdout", "stderr", 0)
    mock_extract_frontend.return_value = ([], "")

    main()

    # Both are matched successfully and approved, so it should pass!
    mock_exit.assert_not_called()


@patch("scripts.validate_vulnerabilities.subprocess.run")
@patch("scripts.validate_vulnerabilities.os.unlink")
def test_execute_pip_audit_success(mock_unlink, mock_run):
    """Verify that execute_pip_audit correctly exports dependencies and audits them, then cleans up."""
    from unittest.mock import MagicMock

    from scripts.validate_vulnerabilities import execute_pip_audit

    mock_run_res_export = MagicMock()
    mock_run_res_export.returncode = 0
    mock_run_res_export.stdout = "exported"
    mock_run_res_export.stderr = ""

    mock_run_res_audit = MagicMock()
    mock_run_res_audit.returncode = 0
    mock_run_res_audit.stdout = '{"dependencies": []}'
    mock_run_res_audit.stderr = ""

    mock_run.side_effect = [mock_run_res_export, mock_run_res_audit]

    stdout, stderr, code = execute_pip_audit()

    assert stdout == '{"dependencies": []}'
    assert stderr == ""
    assert code == 0

    # Ensure two subprocess runs were executed
    assert mock_run.call_count == 2

    # Check export command arguments
    export_args = mock_run.call_args_list[0][0][0]
    assert "uv" in export_args
    assert "export" in export_args
    assert "--no-dev" in export_args
    assert "-o" in export_args

    # Check audit command arguments
    audit_args = mock_run.call_args_list[1][0][0]
    assert "pip-audit" in audit_args
    assert "-r" in audit_args

    # Ensure the temp file was unlinked
    mock_unlink.assert_called_once()


@patch("scripts.validate_vulnerabilities.subprocess.run")
@patch("scripts.validate_vulnerabilities.shutil.which")
def test_execute_pnpm_audit_success(mock_which, mock_run):
    """Verify that execute_pnpm_audit executes with the --prod flag to isolate production packages."""
    from unittest.mock import MagicMock

    from scripts.validate_vulnerabilities import execute_pnpm_audit

    mock_which.return_value = "/usr/bin/pnpm"
    mock_run_res = MagicMock()
    mock_run_res.returncode = 0
    mock_run_res.stdout = '{"vulnerabilities": []}'
    mock_run_res.stderr = ""
    mock_run.return_value = mock_run_res

    stdout, stderr, code = execute_pnpm_audit()

    assert stdout == '{"vulnerabilities": []}'
    assert stderr == ""
    assert code == 0

    # Check pnpm audit command has --prod flag
    cmd_args = mock_run.call_args[0][0]
    assert "pnpm" in cmd_args
    assert "audit" in cmd_args
    assert "--json" in cmd_args
    assert "--prod" in cmd_args


def test_extract_active_frontend_vulnerabilities_modern_v9():
    """Verify that simulated modern pnpm v9 vulnerability payloads are correctly parsed."""
    sample_audit = {
        "vulnerabilities": {
            "esbuild": {
                "name": "esbuild",
                "severity": "high",
                "via": [
                    {
                        "source": 1102341,
                        "name": "esbuild",
                        "dependency": "esbuild",
                        "title": "esbuild issue in development server",
                        "url": "https://github.com/advisories/GHSA-67mh-4wv8-2f99",
                        "severity": "high",
                        "cves": ["CVE-2024-9999"],
                        "range": "<0.24.3",
                    }
                ],
                "effects": [],
                "range": "<0.24.3",
                "nodes": ["node_modules/esbuild"],
                "dependency": "esbuild",
            },
            "ip": {
                "name": "ip",
                "severity": "high",
                "via": [
                    {
                        "source": 1096338,
                        "name": "ip",
                        "dependency": "ip",
                        "title": "ip address amplification",
                        "url": "https://github.com/advisories/GHSA-2p57-rm97-gv6v",
                        "severity": "high",
                        "cves": ["CVE-2024-29415"],
                        "range": "<1.1.9 || >=2.0.0 <2.0.1",
                    }
                ],
                "effects": [],
                "range": "<1.1.9 || >=2.0.0 <2.0.1",
            },
        }
    }
    vulns, err = extract_active_frontend_vulnerabilities(json.dumps(sample_audit))
    assert not err
    assert len(vulns) == 2

    # Assert esbuild vulnerability details
    esbuild_vuln = [v for v in vulns if v["package_name"] == "esbuild"][0]
    assert esbuild_vuln["vulnerability_id"] == "GHSA-67MH-4WV8-2F99"
    assert esbuild_vuln["version"] == "unknown"
    assert esbuild_vuln["description"] == "esbuild issue in development server"
    assert esbuild_vuln["fix_versions"] == ">=0.24.3"

    # Assert ip vulnerability details
    ip_vuln = [v for v in vulns if v["package_name"] == "ip"][0]
    assert ip_vuln["vulnerability_id"] == "GHSA-2P57-RM97-GV6V"
    assert ip_vuln["version"] == "unknown"
    assert ip_vuln["description"] == "ip address amplification"
    assert ip_vuln["fix_versions"] == ">=1.1.9 || >=2.0.1"


def test_scan_for_config_bypasses_no_violations(tmp_path):
    """Verify scan_for_config_bypasses returns no violations when none exist."""
    clean_npmrc = tmp_path / ".npmrc"
    clean_npmrc.write_text("registry=https://registry.npmjs.org/\n", encoding="utf-8")

    clean_pnpmrc = tmp_path / ".pnpmrc"
    clean_pnpmrc.write_text("shamefully-hoist=true\n", encoding="utf-8")

    with patch("scripts.validate_vulnerabilities.REPO_ROOT", str(tmp_path)):
        violations = scan_for_config_bypasses()
        assert len(violations) == 0


def test_scan_for_config_bypasses_with_violations(tmp_path):
    """Verify scan_for_config_bypasses identifies audit bypass options in .npmrc/.pnpmrc."""
    dirty_npmrc = tmp_path / ".npmrc"
    dirty_npmrc.write_text("audit=false\n", encoding="utf-8")

    dirty_pnpmrc = tmp_path / ".pnpmrc"
    dirty_pnpmrc.write_text("audit-level=high\n", encoding="utf-8")

    with patch("scripts.validate_vulnerabilities.REPO_ROOT", str(tmp_path)):
        violations = scan_for_config_bypasses()
        assert len(violations) == 2
        paths = [v[0] for v in violations]
        assert str(dirty_npmrc) in paths
        assert str(dirty_pnpmrc) in paths


@patch(
    "scripts.validate_vulnerabilities.sys.argv",
    ["validate_vulnerabilities.py", "--skip-audit"],
)
@patch("scripts.validate_vulnerabilities.sys.exit", side_effect=SystemExit(1))
def test_cli_bypass_blocking(mock_exit):
    """Verify that command-line bypass attempts trigger immediate execution failure."""
    import pytest

    mock_exit.side_effect = SystemExit(1)
    from scripts.validate_vulnerabilities import main

    with pytest.raises(SystemExit) as exc_info:
        main()
    assert exc_info.value.code == 1
    mock_exit.assert_called_once_with(1)


def test_scan_for_inline_bypasses_multiline_consecutive(tmp_path):
    """Verify that multi-line bypass on consecutive lines is detected."""
    (tmp_path / "apps").mkdir()
    file = tmp_path / "apps" / "workflow.yml"
    file.write_text("run: |\n  pip-audit \\\n    -i CVE-12345\n", encoding="utf-8")
    with patch("scripts.validate_vulnerabilities.REPO_ROOT", str(tmp_path)):
        violations = scan_for_inline_bypasses()
        assert len(violations) == 1
        assert str(file) == violations[0][0]


def test_scan_for_inline_bypasses_multiline_three_lines(tmp_path):
    """Verify that multi-line bypass within 3 lines is detected."""
    (tmp_path / "apps").mkdir()
    file = tmp_path / "apps" / "script.sh"
    file.write_text(
        "pip-audit \\\n  --some-other-arg \\\n  --ignore-vuln CVE-9999\n",
        encoding="utf-8",
    )
    with patch("scripts.validate_vulnerabilities.REPO_ROOT", str(tmp_path)):
        violations = scan_for_inline_bypasses()
        assert len(violations) == 1
        assert str(file) == violations[0][0]


def test_scan_for_inline_bypasses_multiline_out_of_scope(tmp_path):
    """Verify that multi-line bypass over wider interval (4 lines) is not detected (out of scope)."""
    (tmp_path / "apps").mkdir()
    file = tmp_path / "apps" / "script.sh"
    file.write_text(
        "pip-audit \\\n  --arg1 \\\n  --arg2 \\\n  --ignore-vuln CVE-9999\n",
        encoding="utf-8",
    )
    with patch("scripts.validate_vulnerabilities.REPO_ROOT", str(tmp_path)):
        violations = scan_for_inline_bypasses()
        assert len(violations) == 0


def test_scan_for_inline_bypasses_logical_boundary_reset(tmp_path):
    """Verify that a logical YAML boundary delimiter resets sliding buffer."""
    (tmp_path / "apps").mkdir()
    file = tmp_path / "apps" / "workflow.yml"
    file.write_text(
        "- name: Step 1\n"
        "  run: pip-audit\n"
        "- name: Step 2\n"
        "  run: curl -i https://example.com\n",
        encoding="utf-8",
    )
    with patch("scripts.validate_vulnerabilities.REPO_ROOT", str(tmp_path)):
        violations = scan_for_inline_bypasses()
        assert len(violations) == 0


def test_scan_for_inline_bypasses_shell_boundary_reset(tmp_path):
    """Verify that a standard shell execution boundary resets sliding buffer."""
    (tmp_path / "apps").mkdir()
    file = tmp_path / "apps" / "script.sh"
    file.write_text("pip-audit\ncurl -i https://example.com\n", encoding="utf-8")
    with patch("scripts.validate_vulnerabilities.REPO_ROOT", str(tmp_path)):
        violations = scan_for_inline_bypasses()
        assert len(violations) == 0


def test_scan_for_inline_bypasses_same_line_boundary_reset(tmp_path):
    """Verify that a shell boundary on the same line resets buffer and prevents false positive."""
    (tmp_path / "apps").mkdir()
    file = tmp_path / "apps" / "script.sh"
    file.write_text("pip-audit && curl -i https://example.com\n", encoding="utf-8")
    with patch("scripts.validate_vulnerabilities.REPO_ROOT", str(tmp_path)):
        violations = scan_for_inline_bypasses()
        assert len(violations) == 0


def test_scan_for_inline_bypasses_yaml_folded_vs_literal(tmp_path):
    """Verify that YAML folded block (>) triggers but literal (|) doesn't (since literal requires \\)."""
    (tmp_path / "apps").mkdir()

    # 1. Folded block (>) -> triggers even without trailing backslashes since it folds into a single line
    folded_file = tmp_path / "apps" / "folded.yml"
    folded_file.write_text(
        "- name: Step\n  run: >\n    pip-audit\n    --ignore-vuln CVE-12345\n",
        encoding="utf-8",
    )

    # 2. Literal block (|) -> does not trigger because they are on separate lines with no backslashes
    literal_file = tmp_path / "apps" / "literal.yml"
    literal_file.write_text(
        '- name: Step\n  run: |\n    pip-audit\n    grep -i "pattern"\n',
        encoding="utf-8",
    )

    with patch("scripts.validate_vulnerabilities.REPO_ROOT", str(tmp_path)):
        violations = scan_for_inline_bypasses()
        assert len(violations) == 1
        assert str(folded_file) == violations[0][0]


def test_scan_for_inline_bypasses_comments_and_empty_lines(tmp_path):
    """Verify that shell comments and empty lines are ignored and do not interfere with detection."""
    (tmp_path / "apps").mkdir()
    file = tmp_path / "apps" / "script.sh"
    file.write_text(
        "pip-audit \\\n  # some comment in the middle\n\n  -i CVE-12345\n",
        encoding="utf-8",
    )
    with patch("scripts.validate_vulnerabilities.REPO_ROOT", str(tmp_path)):
        violations = scan_for_inline_bypasses()
        assert len(violations) == 1
        assert str(file) == violations[0][0]
