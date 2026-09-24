"""Unit and CLI integration tests for cadence settings export commands."""

import json

from typer.testing import CliRunner

from packages.cli.main import app

runner = CliRunner()


def test_cadence_settings_export_help():
    """Verify settings export help menu rendering."""
    result = runner.invoke(app, ["settings", "export", "--help"])
    assert result.exit_code == 0
    assert "Export Pydantic settings models" in result.output


def test_cadence_settings_export_check():
    """Verify cadence settings export --check runs and returns 0 on synced template."""
    result = runner.invoke(app, ["settings", "export", "--check"])
    assert result.exit_code == 0


def test_cadence_settings_export_json():
    """Verify cadence settings export --json emits machine-readable JSON output."""
    result = runner.invoke(app, ["settings", "export", "--check", "--json"])
    assert result.exit_code == 0
    data = json.loads(result.output)
    assert data["command"] == "settings export"
    assert data["check"] is True
    assert data["success"] is True
    assert "duration_seconds" in data
