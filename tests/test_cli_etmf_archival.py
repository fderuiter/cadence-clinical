import subprocess
import sys


def test_rtm_generation_with_cli_overrides(tmp_path):
    output_dir = tmp_path / "custom_reports"

    cmd = [
        sys.executable,
        "scripts/generate_rtm.py",
        "--output-dir",
        str(output_dir),
        "--dynamic-timestamp",
    ]

    result = subprocess.run(cmd, capture_output=True, text=True, check=True)
    assert result.returncode == 0

    # Assert reports exist
    rtm_file = output_dir / "Requirements_Traceability_Matrix.md"
    qual_file = output_dir / "IQ_OQ_PQ_Execution_Report.md"

    assert rtm_file.is_file()
    assert qual_file.is_file()

    # Check that they have the actual dynamic timestamp (not the baseline static one)
    baseline_timestamp = "2026-07-23 22:38:25 UTC"

    rtm_content = rtm_file.read_text(encoding="utf-8")
    qual_content = qual_file.read_text(encoding="utf-8")

    assert baseline_timestamp not in rtm_content
    assert baseline_timestamp not in qual_content

    # Assert correct dynamic date patterns or content
    assert "*Generated on:*" in rtm_content or "Generated on" in rtm_content


def test_rtm_generation_conftest_hook_detection(tmp_path, monkeypatch):
    # Test that conftest's session finish environment variables are respected
    output_dir = tmp_path / "conftest_reports"
    monkeypatch.setenv("RTM_OUTPUT_DIR", str(output_dir))
    monkeypatch.setenv("RTM_DYNAMIC_TIMESTAMP", "true")

    # Import conftest to run its hook function
    from tests.conftest import pytest_sessionfinish

    class MockSession:
        pass

    pytest_sessionfinish(MockSession(), 0)

    rtm_file = output_dir / "Requirements_Traceability_Matrix.md"
    qual_file = output_dir / "IQ_OQ_PQ_Execution_Report.md"

    assert rtm_file.is_file()
    assert qual_file.is_file()

    rtm_content = rtm_file.read_text(encoding="utf-8")
    baseline_timestamp = "2026-07-23 22:38:25 UTC"
    assert baseline_timestamp not in rtm_content
