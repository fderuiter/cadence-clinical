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
)


def test_scan_for_inline_bypasses_no_violations(tmp_path):
    """Verify that scan_for_inline_bypasses returns no violations when none exist."""
    # Create temp directory layout matching scan paths
    workflows_dir = tmp_path / ".github" / "workflows"
    workflows_dir.mkdir(parents=True)

    workflow_file = workflows_dir / "ci_clean.yml"
    workflow_file.write_text("run: uv run pip-audit\n", encoding="utf-8")

    with patch("scripts.validate_vulnerabilities.os.path.exists", return_value=True):
        with patch("scripts.validate_vulnerabilities.os.walk") as mock_walk:

            def mock_walk_fn(path, *args, **kwargs):
                if ".github/workflows" in path:
                    return [(str(workflows_dir), [], ["ci_clean.yml"])]
                return []

            mock_walk.side_effect = mock_walk_fn

            # Use open() patch to direct to our temporary file
            orig_open = open

            def mock_open(file, *args, **kwargs):
                if "ci_clean.yml" in str(file):
                    return orig_open(workflow_file, *args, **kwargs)
                return orig_open(file, *args, **kwargs)

            with patch("builtins.open", mock_open):
                violations = scan_for_inline_bypasses()
                assert len(violations) == 0


def test_scan_for_inline_bypasses_with_violations(tmp_path):
    """Verify that scan_for_inline_bypasses identifies --ignore-vuln flag violations."""
    workflows_dir = tmp_path / ".github" / "workflows"
    workflows_dir.mkdir(parents=True)

    workflow_file = workflows_dir / "ci_dirty.yml"
    workflow_file.write_text(
        "run: uv run pip-audit --ignore-vuln CVE-12345\n", encoding="utf-8"
    )

    with patch("scripts.validate_vulnerabilities.os.path.exists", return_value=True):
        with patch("scripts.validate_vulnerabilities.os.walk") as mock_walk:

            def mock_walk_fn(path, *args, **kwargs):
                if ".github/workflows" in path:
                    return [(str(workflows_dir), [], ["ci_dirty.yml"])]
                return []

            mock_walk.side_effect = mock_walk_fn

            orig_open = open

            def mock_open(file, *args, **kwargs):
                if "ci_dirty.yml" in str(file):
                    return orig_open(workflow_file, *args, **kwargs)
                return orig_open(file, *args, **kwargs)

            with patch("builtins.open", mock_open):
                violations = scan_for_inline_bypasses()
                assert len(violations) == 1
                assert "ci_dirty.yml" in violations[0][0]
                assert violations[0][1] == 1
                assert "--ignore-vuln" in violations[0][2]


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
