"""Unit tests for cadence record CLI command.

@req:PRD-SYS-105
"""

import json
from pathlib import Path
from unittest.mock import MagicMock, patch

from typer.testing import CliRunner

from packages.cli.main import app

runner = CliRunner()


def test_record_help():
    """Verify cadence record --help outputs all subcommands and descriptions.

    @req:PRD-SYS-105
    """
    result = runner.invoke(app, ["record", "--help"])
    assert result.exit_code == 0
    assert "run" in result.output
    assert "setup-gdrive" in result.output
    assert "list" in result.output


def test_record_list_empty(tmp_path: Path, monkeypatch):
    """Verify record list when no recordings exist.

    @req:PRD-SYS-105
    """
    monkeypatch.setattr("packages.cli.commands.record.RECORDINGS_DIR", tmp_path)
    result = runner.invoke(app, ["record", "list"])
    assert result.exit_code == 0
    assert "No local recordings found" in result.output or "[]" in result.output


def test_record_list_with_runs(tmp_path: Path, monkeypatch):
    """Verify record list renders existing recording directories.

    @req:PRD-SYS-105
    """
    run_dir = tmp_path / "Cadence_Run_2026-08-23"
    run_dir.mkdir(parents=True)
    (run_dir / "01_MDR.webm").write_bytes(b"dummy")
    (run_dir / "run_manifest.json").write_text(
        json.dumps({"manifest_version": "1.0", "files": []})
    )

    monkeypatch.setattr("packages.cli.commands.record.RECORDINGS_DIR", tmp_path)
    result = runner.invoke(app, ["record", "list"])
    assert result.exit_code == 0
    assert "VERIFIED" in result.output

    # Also test json output
    json_res = runner.invoke(app, ["--json", "record", "list"])
    assert json_res.exit_code == 0
    data = json.loads(json_res.output)
    assert len(data) == 1
    assert data[0]["run_dir"] == "Cadence_Run_2026-08-23"


@patch("packages.cli.commands.record.GoogleDriveClient")
def test_record_setup_gdrive_valid(mock_gdrive_cls, tmp_path: Path):
    """Verify setup-gdrive validates credentials and folder access.

    @req:PRD-SYS-105
    """
    fake_key = tmp_path / "service_account.json"
    fake_key.write_text("{}")

    mock_client = MagicMock()
    mock_client.is_configured = True
    mock_client.validate_credentials.return_value = {
        "valid": True,
        "email": "test-bot@gserviceaccount.com",
        "displayName": "Test Bot",
    }
    mock_gdrive_cls.return_value = mock_client

    result = runner.invoke(
        app,
        [
            "record",
            "setup-gdrive",
            "--credentials-path",
            str(fake_key),
            "--folder-id",
            "test_folder_123",
        ],
    )
    assert result.exit_code == 0
    assert "Successfully verified Google Drive credentials" in result.output


@patch("packages.cli.commands.record.subprocess.run")
@patch("packages.cli.commands.record.generate_run_manifest")
def test_record_run_local_only(
    mock_manifest, mock_subproc, tmp_path: Path, monkeypatch
):
    """Verify record run in local-only mode executes test suite and creates manifest.

    @req:PRD-SYS-105
    """
    monkeypatch.setattr("packages.cli.commands.record.RECORDINGS_DIR", tmp_path)
    mock_subproc.return_value = MagicMock(
        returncode=0, stdout="Playwright suite passed", stderr=""
    )
    mock_manifest.return_value = {"manifest_version": "1.0", "files": []}

    result = runner.invoke(
        app, ["record", "run", "--local-only", "--suite", "designer", "--fast"]
    )
    assert result.exit_code == 0
    assert (
        "Triple Sentinel Verification Passed" in result.output
        or "completed" in result.output.lower()
    )
