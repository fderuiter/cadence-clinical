"""Unit tests for GxP Sync Preflight Assembly Verification and Dynamic Build Execution.

@req:PRD-SYS-001
"""

import os
import sys
import tempfile
import time
from pathlib import Path

# Ensure repository root is in sys.path
REPO_ROOT = Path(__file__).resolve().parents[2]
if str(REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(REPO_ROOT))

from scripts.sync_gxp import (
    check_test_assemblies_status,
    get_required_test_assemblies,
)


def test_get_required_test_assemblies_discovery():
    """Verify get_required_test_assemblies locates the expected test DLL paths.

    @req:PRD-SYS-001
    """
    assemblies = get_required_test_assemblies(REPO_ROOT, "Release")
    assert len(assemblies) >= 3
    assembly_names = [p.name for p in assemblies]
    assert "Cadence.UnitTests.dll" in assembly_names
    assert "Cadence.ArchitectureTests.dll" in assembly_names
    assert "Cadence.E2ETests.dll" in assembly_names


def test_check_test_assemblies_status_missing():
    """Verify missing status is reported when test assemblies do not exist.

    @req:PRD-SYS-001
    """
    with tempfile.TemporaryDirectory() as tmp_dir:
        tmp_root = Path(tmp_dir)
        tests_dir = tmp_root / "tests"
        tests_dir.mkdir()
        (tests_dir / "Dummy.UnitTests.csproj").write_text(
            "<Project><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup></Project>",
            encoding="utf-8",
        )

        missing, stale, required = check_test_assemblies_status(
            tmp_root, configuration="Release"
        )
        assert missing is True
        assert stale is False
        assert len(required) >= 1


def test_check_test_assemblies_status_fresh_and_stale():
    """Verify fresh vs stale status calculation based on assembly mtime vs source file mtime.

    @req:PRD-SYS-001
    """
    with tempfile.TemporaryDirectory() as tmp_dir:
        tmp_root = Path(tmp_dir)
        tests_dir = tmp_root / "tests"
        tests_dir.mkdir(parents=True)
        (tests_dir / "TestProj.csproj").write_text(
            "<Project><PropertyGroup><IsTestProject>true</IsTestProject><BaseOutputPath>bin/</BaseOutputPath></PropertyGroup></Project>",
            encoding="utf-8",
        )

        # Create dummy source file
        apps_dir = tmp_root / "apps" / "dummy"
        apps_dir.mkdir(parents=True)
        src_file = apps_dir / "Service.cs"
        src_file.write_text("// dummy C# code", encoding="utf-8")

        # Create dummy assembly DLL
        dll_dir = tests_dir / "bin" / "Release" / "net10.0"
        dll_dir.mkdir(parents=True)
        dll_file = dll_dir / "TestProj.dll"
        dll_file.write_text("binary", encoding="utf-8")

        # Set assembly mtime to current time
        now = time.time()
        os.utime(dll_file, (now, now))
        # Set source file mtime to 10 seconds in the past
        os.utime(src_file, (now - 10, now - 10))

        # 1. Fresh state
        missing, stale, _ = check_test_assemblies_status(
            tmp_root, configuration="Release"
        )
        assert missing is False
        assert stale is False

        # 2. Make source file newer than assembly (stale state)
        os.utime(src_file, (now + 10, now + 10))
        missing, stale, _ = check_test_assemblies_status(
            tmp_root, configuration="Release"
        )
        assert missing is False
        assert stale is True


def test_generate_rtm_missing_or_empty_report_error():
    """Verify generate_rtm.py errors out when report_dotnet.trx is 0 bytes or missing without --draft.

    @req:PRD-SYS-001
    """
    import subprocess

    gen_script = REPO_ROOT / "scripts" / "generate_rtm.py"

    with tempfile.TemporaryDirectory() as tmp_dir:
        empty_report = Path(tmp_dir) / "report_dotnet.trx"
        empty_report.write_bytes(b"")  # 0 bytes

        result = subprocess.run(
            [
                sys.executable,
                str(gen_script),
                "--report",
                str(empty_report),
                "--output-dir",
                str(Path(tmp_dir) / "docs"),
            ],
            cwd=str(REPO_ROOT),
            capture_output=True,
            text=True,
        )

        assert result.returncode != 0
        assert "ERROR: Required test report" in result.stderr
        assert "missing or empty" in result.stderr
