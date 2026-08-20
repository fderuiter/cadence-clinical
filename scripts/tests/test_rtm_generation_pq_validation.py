import subprocess
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent.parent


def test_pq_all_tests_passed(tmp_path):
    """Test that all PQ scenarios are marked as Verified Compliant if all tests pass."""
    xml_content = """<?xml version="1.0" encoding="utf-8"?>
<testsuites>
  <testsuite name="pytest" errors="0" failures="0" skipped="0" tests="4" time="0.100" timestamp="2026-08-17T12:00:00">
    <testcase classname="apps.execution.tests.test_study_versions" name="test_api_protocol_approval_and_immutability" time="0.010" />
    <testcase classname="apps.execution.tests.test_subject_randomization_lifecycle" name="test_stratification_factors_locking" time="0.010" />
    <testcase classname="apps.interop.tests.test_offline_sync" name="test_offline_sync_conflict_resolution" time="0.010" />
    <testcase classname="apps.safety.tests.test_emergency_unblinding" name="test_unblind_success_authorized_access" time="0.010" />
  </testsuite>
</testsuites>
"""
    report_file = tmp_path / "report.xml"
    report_file.write_text(xml_content, encoding="utf-8")

    output_dir = tmp_path / "reports_all_passed"
    cmd = [
        sys.executable,
        "scripts/generate_rtm.py",
        "--report-path",
        str(report_file),
        "--output-dir",
        str(output_dir),
    ]

    res = subprocess.run(cmd, capture_output=True, text=True, cwd=str(REPO_ROOT))
    assert res.returncode == 0, f"Script failed with: {res.stderr}"

    qual_file = output_dir / "IQ_OQ_PQ_Execution_Report.md"
    assert qual_file.is_file()
    qual_content = qual_file.read_text(encoding="utf-8")

    assert (
        "### TC-VAL-LOG-001: Protocol Version Locking & Immutability Rejection"
        in qual_content
    )
    assert (
        "Verification Status:** ✅ Verified Compliant via Automated Integration Suite"
        in qual_content
    )


def test_pq_test_failed(tmp_path):
    """Test that if a mapped test fails, the associated scenario is marked as failed."""
    xml_content = """<?xml version="1.0" encoding="utf-8"?>
<testsuites>
  <testsuite name="pytest" errors="0" failures="1" skipped="0" tests="4" time="0.100" timestamp="2026-08-17T12:00:00">
    <testcase classname="apps.execution.tests.test_study_versions" name="test_api_protocol_approval_and_immutability" time="0.010">
      <failure message="immutability failed">assertion error</failure>
    </testcase>
    <testcase classname="apps.execution.tests.test_subject_randomization_lifecycle" name="test_stratification_factors_locking" time="0.010" />
    <testcase classname="apps.interop.tests.test_offline_sync" name="test_offline_sync_conflict_resolution" time="0.010" />
    <testcase classname="apps.safety.tests.test_emergency_unblinding" name="test_unblind_success_authorized_access" time="0.010" />
  </testsuite>
</testsuites>
"""
    report_file = tmp_path / "report.xml"
    report_file.write_text(xml_content, encoding="utf-8")

    output_dir = tmp_path / "reports_failed"
    cmd = [
        sys.executable,
        "scripts/generate_rtm.py",
        "--report-path",
        str(report_file),
        "--output-dir",
        str(output_dir),
    ]

    res = subprocess.run(cmd, capture_output=True, text=True, cwd=str(REPO_ROOT))
    assert res.returncode == 0, f"Script failed with: {res.stderr}"

    qual_file = output_dir / "IQ_OQ_PQ_Execution_Report.md"
    assert qual_file.is_file()
    qual_content = qual_file.read_text(encoding="utf-8")

    assert (
        "### TC-VAL-LOG-001: Protocol Version Locking & Immutability Rejection"
        in qual_content
    )
    assert (
        "Verification Status:** ❌ Failed via Automated Integration Suite"
        in qual_content
    )
    # Mapped test for TC-VAL-LOG-002 still passes
    assert (
        "### TC-VAL-LOG-002: Stratification Factor Re-randomization Rejections"
        in qual_content
    )
    assert (
        "Verification Status:** ✅ Verified Compliant via Automated Integration Suite"
        in qual_content
    )


def test_pq_test_skipped(tmp_path):
    """Test that if a mapped test is skipped, the associated scenario is marked as skipped."""
    xml_content = """<?xml version="1.0" encoding="utf-8"?>
<testsuites>
  <testsuite name="pytest" errors="0" failures="0" skipped="1" tests="4" time="0.100" timestamp="2026-08-17T12:00:00">
    <testcase classname="apps.execution.tests.test_study_versions" name="test_api_protocol_approval_and_immutability" time="0.010">
      <skipped message="skipping for test" />
    </testcase>
    <testcase classname="apps.execution.tests.test_subject_randomization_lifecycle" name="test_stratification_factors_locking" time="0.010" />
    <testcase classname="apps.interop.tests.test_offline_sync" name="test_offline_sync_conflict_resolution" time="0.010" />
    <testcase classname="apps.safety.tests.test_emergency_unblinding" name="test_unblind_success_authorized_access" time="0.010" />
  </testsuite>
</testsuites>
"""
    report_file = tmp_path / "report.xml"
    report_file.write_text(xml_content, encoding="utf-8")

    output_dir = tmp_path / "reports_skipped"
    cmd = [
        sys.executable,
        "scripts/generate_rtm.py",
        "--report-path",
        str(report_file),
        "--output-dir",
        str(output_dir),
    ]

    res = subprocess.run(cmd, capture_output=True, text=True, cwd=str(REPO_ROOT))
    assert res.returncode == 0, f"Script failed with: {res.stderr}"

    qual_file = output_dir / "IQ_OQ_PQ_Execution_Report.md"
    assert qual_file.is_file()
    qual_content = qual_file.read_text(encoding="utf-8")

    assert (
        "### TC-VAL-LOG-001: Protocol Version Locking & Immutability Rejection"
        in qual_content
    )
    assert (
        "Verification Status:** ⚪ Skipped via Automated Integration Suite"
        in qual_content
    )


def test_pq_test_missing_fail_fast(tmp_path):
    """Test that if a mapped test is missing, report generation fails and raises an error (unless in draft)."""
    xml_content = """<?xml version="1.0" encoding="utf-8"?>
<testsuites>
  <testsuite name="pytest" errors="0" failures="0" skipped="0" tests="3" time="0.100" timestamp="2026-08-17T12:00:00">
    <testcase classname="apps.execution.tests.test_subject_randomization_lifecycle" name="test_stratification_factors_locking" time="0.010" />
    <testcase classname="apps.interop.tests.test_offline_sync" name="test_offline_sync_conflict_resolution" time="0.010" />
    <testcase classname="apps.safety.tests.test_emergency_unblinding" name="test_unblind_success_authorized_access" time="0.010" />
  </testsuite>
</testsuites>
"""
    report_file = tmp_path / "report.xml"
    report_file.write_text(xml_content, encoding="utf-8")

    output_dir = tmp_path / "reports_missing"
    cmd = [
        sys.executable,
        "scripts/generate_rtm.py",
        "--report-path",
        str(report_file),
        "--output-dir",
        str(output_dir),
    ]

    res = subprocess.run(cmd, capture_output=True, text=True, cwd=str(REPO_ROOT))
    # Script must fail because a mapped test is missing
    assert res.returncode != 0
    assert (
        "ERROR: Active test" in res.stderr
        or "missing from the test results report" in res.stderr
        or "ValueError" in res.stderr
    )


def test_pq_test_missing_draft_mode(tmp_path):
    """Test that draft mode bypasses the missing mapped test fail-fast check and outputs UNVERIFIED."""
    xml_content = """<?xml version="1.0" encoding="utf-8"?>
<testsuites>
  <testsuite name="pytest" errors="0" failures="0" skipped="0" tests="3" time="0.100" timestamp="2026-08-17T12:00:00">
    <testcase classname="apps.execution.tests.test_subject_randomization_lifecycle" name="test_stratification_factors_locking" time="0.010" />
    <testcase classname="apps.interop.tests.test_offline_sync" name="test_offline_sync_conflict_resolution" time="0.010" />
    <testcase classname="apps.safety.tests.test_emergency_unblinding" name="test_unblind_success_authorized_access" time="0.010" />
  </testsuite>
</testsuites>
"""
    report_file = tmp_path / "report.xml"
    report_file.write_text(xml_content, encoding="utf-8")

    output_dir = tmp_path / "reports_draft"
    cmd = [
        sys.executable,
        "scripts/generate_rtm.py",
        "--report-path",
        str(report_file),
        "--output-dir",
        str(output_dir),
        "--draft",
    ]

    res = subprocess.run(cmd, capture_output=True, text=True, cwd=str(REPO_ROOT))
    assert res.returncode == 0, f"Script failed with: {res.stderr}"

    qual_file = output_dir / "IQ_OQ_PQ_Execution_Report.md"
    assert qual_file.is_file()
    qual_content = qual_file.read_text(encoding="utf-8")

    assert (
        "### TC-VAL-LOG-001: Protocol Version Locking & Immutability Rejection"
        in qual_content
    )
    assert "Verification Status:** ⚪ Unverified (Draft Mode)" in qual_content


def test_rtm_adr_traceability_table(tmp_path):
    """Test that RTM generation outputs Architectural Decisions Traceability Table and writes RTM.md."""
    output_dir = tmp_path / "reports_adr"
    cmd = [
        sys.executable,
        "scripts/generate_rtm.py",
        "--output-dir",
        str(output_dir),
        "--draft",
    ]

    res = subprocess.run(cmd, capture_output=True, text=True, cwd=str(REPO_ROOT))
    assert res.returncode == 0, f"Script failed with: {res.stderr}"

    rtm_matrix_file = output_dir / "Requirements_Traceability_Matrix.md"
    rtm_short_file = output_dir / "RTM.md"

    assert rtm_matrix_file.is_file()
    assert rtm_short_file.is_file()

    matrix_content = rtm_matrix_file.read_text(encoding="utf-8")
    short_content = rtm_short_file.read_text(encoding="utf-8")

    assert "## 3. Architectural Decisions Traceability Table" in matrix_content
    assert "ADR File" in matrix_content
    assert "Decision Title" in matrix_content
    assert "2026-06-06-usdm-pydantic-models.md" in matrix_content
    assert "PRD-MDR-001" in matrix_content
    assert matrix_content == short_content
