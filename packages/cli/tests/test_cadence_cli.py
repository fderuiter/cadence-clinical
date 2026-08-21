"""Unit tests for the Cadence CLI commands and formatting."""

import json
from pathlib import Path

from typer.testing import CliRunner

from packages.cli.commands.test import _find_target_test_file
from packages.cli.main import app

runner = CliRunner()


def test_cli_help():
    """Verify that root CLI help outputs commands properly."""
    result = runner.invoke(app, ["--help"])
    assert result.exit_code == 0
    assert "Cadence Clinical" in result.output
    assert "doctor" in result.output
    assert "dev" in result.output
    assert "test" in result.output
    assert "check" in result.output
    assert "fix" in result.output
    assert "db" in result.output
    assert "scaffold" in result.output
    assert "gxp" in result.output
    assert "cdisc" in result.output


def test_cli_cdisc_export_json(tmp_path: Path):
    """Verify cadence cdisc export --json outputs structured USDM document."""
    out_file = tmp_path / "cdisc_export_test.json"
    result = runner.invoke(
        app, ["--json", "cdisc", "export", "--output", str(out_file)]
    )
    assert result.exit_code == 0
    data = json.loads(result.output)
    assert data["command"] == "cdisc export"
    assert data["success"] is True
    assert data["usdm_version"] == "3.0"
    assert data["biomedical_concepts_count"] > 0
    assert out_file.exists()


def test_cli_gxp_export_cdisc_json(tmp_path: Path):
    """Verify cadence gxp export-cdisc --json outputs structured USDM document."""
    out_file = tmp_path / "gxp_export_test.json"
    result = runner.invoke(
        app, ["--json", "gxp", "export-cdisc", "--output", str(out_file)]
    )
    assert result.exit_code == 0
    data = json.loads(result.output)
    assert data["command"] == "gxp export-cdisc"
    assert data["success"] is True
    assert data["usdm_version"] == "3.0"
    assert data["biomedical_concepts_count"] > 0
    assert out_file.exists()


def test_cli_doctor_json():
    """Verify doctor command runs and produces valid JSON."""
    result = runner.invoke(app, ["--json", "doctor"])
    assert result.exit_code == 0
    data = json.loads(result.output)
    assert "status" in data
    assert "python" in data
    assert "binaries" in data
    assert "databases" in data
    assert "ports" in data


def test_cli_doctor_auto_fix_json():
    """Verify doctor --auto-fix remediates environment and emits JSON."""
    result = runner.invoke(app, ["--json", "doctor", "--auto-fix"])
    assert result.exit_code == 0
    data = json.loads(result.output)
    assert "auto_healed" in data


def test_cli_dev_json():
    """Verify dev command json output returns service manifest."""
    result = runner.invoke(app, ["--json", "dev", "gateway", "execution"])
    assert result.exit_code == 0
    data = json.loads(result.output)
    assert data["status"] == "ready"
    assert len(data["services"]) == 2


def test_cli_dev_port_collision_fallback():
    """Verify dev command automatically assigns fallback offset ports when default ports are occupied."""
    import socket

    s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    s.bind(("127.0.0.1", 8000))
    s.listen(1)
    try:
        result = runner.invoke(app, ["--json", "dev", "gateway"])
        assert result.exit_code == 0
        data = json.loads(result.output)
        assert data["services"][0]["name"] == "gateway"
        assert data["services"][0]["default_port"] == 8000
        assert data["services"][0]["assigned_port"] > 8000
        assert data["services"][0]["rebound"] is True
        assert data["services"][0]["offset"] > 0
    finally:
        s.close()


def test_cli_db_status_json():
    """Verify db status command produces valid JSON."""
    result = runner.invoke(app, ["--json", "db", "status"])
    assert result.exit_code == 0
    data = json.loads(result.output)
    assert "databases" in data
    assert "snapshots" in data


def test_cli_db_snapshot_and_restore():
    """Verify snapshot and restore command workflow."""
    result = runner.invoke(app, ["--json", "db", "snapshot", "test-snapshot-unit"])
    assert result.exit_code == 0
    data = json.loads(result.output)
    assert data["success"] is True

    result_restore = runner.invoke(
        app, ["--json", "db", "restore", "test-snapshot-unit"]
    )
    assert result_restore.exit_code == 0
    data_restore = json.loads(result_restore.output)
    assert data_restore["success"] is True


def test_cli_db_seed_json():
    """Verify db seed command produces structured report."""
    result = runner.invoke(app, ["--json", "db", "seed", "--tier", "subjects"])
    assert result.exit_code == 0
    data = json.loads(result.output)
    assert data["success"] is True
    assert "seeded_entities" in data


def test_find_target_test_file_resolution(tmp_path: Path):
    """Verify smart test file resolver maps source changes to test files."""
    repo = tmp_path
    app_dir = repo / "apps" / "execution"
    test_dir = app_dir / "tests"
    test_dir.mkdir(parents=True)
    src_file = app_dir / "evaluator.py"
    src_file.write_text("# evaluator", encoding="utf-8")
    test_file = test_dir / "test_evaluator.py"
    test_file.write_text("# test", encoding="utf-8")

    res = _find_target_test_file(src_file, repo)
    assert res == "apps/execution/tests/test_evaluator.py"
