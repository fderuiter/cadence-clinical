"""Unit and integration tests for GxP Fail-Fast and Draft Banner Injection.

@req:PRD-SYS-001
"""

import os
import subprocess
import sys
import tempfile
from pathlib import Path

# Paths to the target scripts
GENERATE_RTM_SCRIPT = Path(__file__).resolve().parents[1] / "generate_rtm.py"
SYNC_GXP_SCRIPT = Path(__file__).resolve().parents[1] / "sync_gxp.py"


def test_fail_fast_without_report_and_draft_flag():
    """Verify that generate_rtm.py fails-fast and writes no files when report.xml is missing and draft flag is absent.

    @req:PRD-SYS-001
    """
    from filelock import FileLock

    lock = FileLock("/tmp/gxp_fail_fast_test.lock", timeout=120)
    with lock:
        # Temporarily move report.xml if it exists
        report_path = Path("report.xml")
        backup_path = Path("report.xml.bak_test")
        has_backup = False
        if report_path.exists():
            report_path.rename(backup_path)
            has_backup = True

        with tempfile.TemporaryDirectory() as tmp_dir:
            output_path = Path(tmp_dir) / "SDLC"

            try:
                result = subprocess.run(
                    [
                        sys.executable,
                        str(GENERATE_RTM_SCRIPT),
                        "--output-dir",
                        str(output_path),
                    ],
                    cwd=os.getcwd(),
                    capture_output=True,
                    text=True,
                )

                # Must fail (non-zero exit code)
                assert result.returncode != 0
                assert (
                    "ERROR: Required test report" in result.stderr
                    or "ERROR: Required test report" in result.stdout
                )

                # Must write no files under the output directory
                assert not output_path.exists() or len(os.listdir(output_path)) == 0
            finally:
                if has_backup:
                    backup_path.rename(report_path)


def test_success_with_draft_flag():
    """Verify that generate_rtm.py succeeds and generates draft files with warning banner and UNVERIFIED status when report.xml is missing but draft flag is present.

    @req:PRD-SYS-001
    """
    from filelock import FileLock

    lock = FileLock("/tmp/gxp_fail_fast_test.lock", timeout=120)
    with lock:
        # Temporarily move report.xml if it exists
        report_path = Path("report.xml")
        backup_path = Path("report.xml.bak_test")
        has_backup = False
        if report_path.exists():
            report_path.rename(backup_path)
            has_backup = True

        with tempfile.TemporaryDirectory() as tmp_dir:
            output_path = Path(tmp_dir) / "SDLC"

            try:
                result = subprocess.run(
                    [
                        sys.executable,
                        str(GENERATE_RTM_SCRIPT),
                        "--output-dir",
                        str(output_path),
                        "--draft",
                    ],
                    cwd=os.getcwd(),  # Use repo root so it can scan tests
                    capture_output=True,
                    text=True,
                )

                # Must succeed
                assert result.returncode == 0

                # Output files must exist
                rtm_file = output_path / "Requirements_Traceability_Matrix.md"
                qual_file = output_path / "IQ_OQ_PQ_Execution_Report.md"

                assert rtm_file.exists()
                assert qual_file.exists()

                # Verify RTM file content
                rtm_content = rtm_file.read_text(encoding="utf-8")
                # Check warning banner at the top of RTM file
                assert rtm_content.startswith(
                    "> ⚠️ **DRAFT ONLY — UNVERIFIED GxP COMPLIANCE DOCUMENT** ⚠️"
                )
                # Check that unverified tests are labeled UNVERIFIED instead of PASSED
                assert "⚪ (UNVERIFIED)" in rtm_content

                # Verify Qualification Report file content
                qual_content = qual_file.read_text(encoding="utf-8")
                # Check warning banner at the top of Qualification Report
                assert qual_content.startswith(
                    "> ⚠️ **DRAFT ONLY — UNVERIFIED GxP COMPLIANCE DOCUMENT** ⚠️"
                )
                # Check status and duration are UNVERIFIED and N/A
                assert "⚪ UNVERIFIED" in qual_content
                assert any(part.strip() == "N/A" for part in qual_content.split("|"))
            finally:
                if has_backup:
                    backup_path.rename(report_path)


def test_missing_report_gxp_sync_dry_run():
    """Verify that sync_gxp.py running in dry-run mode passes --draft to generate_rtm.py.

    @req:PRD-SYS-001
    """
    from filelock import FileLock

    lock = FileLock("/tmp/gxp_fail_fast_test.lock", timeout=120)
    with lock:
        # Temporarily move report.xml if it exists
        report_path = Path("report.xml")
        backup_path = Path("report.xml.bak_test")
        has_backup = False
        if report_path.exists():
            report_path.rename(backup_path)
            has_backup = True

        try:
            # Run sync_gxp.py in dry-run mode. Since report.xml is missing,
            # it should run generate_rtm.py with --draft.
            result = subprocess.run(
                [
                    sys.executable,
                    str(SYNC_GXP_SCRIPT),
                    "--dry-run",
                ],
                cwd=os.getcwd(),
                capture_output=True,
                text=True,
            )

            # In dry run mode, sync_gxp.py skips step 1 (running tests).
            # In step 2, it should successfully invoke generate_rtm.py with --draft (because of dry_run).
            assert "Running generate_rtm.py --validate (read-only)." in result.stdout
            assert (
                "uv run --all-extras python scripts/generate_rtm.py --validate --draft"
                in result.stdout
            )
        finally:
            if has_backup:
                backup_path.rename(report_path)
            # Clean up any modified SDLC files in actual repository
            subprocess.run(
                [
                    "git",
                    "checkout",
                    "docs/SDLC/Requirements_Traceability_Matrix.md",
                    "docs/SDLC/IQ_OQ_PQ_Execution_Report.md",
                ],
                check=False,
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
            )
