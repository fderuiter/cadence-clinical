"""Tests for the interactive credential setup wizard script and CLI setup command.

@req:PRD-SYS-WIZARD-001
"""

import json
import subprocess
from pathlib import Path

from typer.testing import CliRunner

from packages.cli.main import app

runner = CliRunner()


def test_setup_wizard_bash_syntax():
    """Verify scripts/setup_credentials.sh passes strict bash -n syntax checking.

    @req:PRD-SYS-WIZARD-001
    """
    repo_root = Path(__file__).resolve().parents[1]
    script_path = repo_root / "scripts" / "setup_credentials.sh"
    assert script_path.exists(), "setup_credentials.sh must exist in scripts/"

    res = subprocess.run(
        ["bash", "-n", str(script_path)],
        capture_output=True,
        text=True,
        check=False,
    )
    assert res.returncode == 0, f"bash -n failed with error: {res.stderr}"


def test_setup_wizard_dev_execution(tmp_path: Path):
    """Verify setup_credentials.sh generates 256-bit keys in automated dev mode.

    @req:PRD-SYS-WIZARD-001
    """
    repo_root = Path(__file__).resolve().parents[1]
    script_path = repo_root / "scripts" / "setup_credentials.sh"
    target_env = tmp_path / ".env.test"

    res = subprocess.run(
        [str(script_path), "--dev", f"--env-file={target_env}"],
        cwd=str(repo_root),
        capture_output=True,
        text=True,
        check=False,
    )
    assert res.returncode == 0, f"Script execution failed: {res.stderr}"
    assert target_env.exists(), "Target environment file was not created"

    content = target_env.read_text()
    assert "APP_ENV=development" in content
    assert "GATEWAY_SECRET=" in content
    assert "AUDIT_LOG_SECRET_KEY=" in content
    assert "SIGNING_SECRET=" in content
    assert "SAFETY_SALT=" in content
    assert "INBOUND_EMAIL_HMAC_SECRET=" in content
    assert "STORAGE_BACKEND=local" in content

    # Verify 256-bit entropy (64 hex characters)
    lines = dict(
        line.split("=", 1) for line in content.strip().splitlines() if "=" in line
    )
    for secret_key in [
        "GATEWAY_SECRET",
        "AUDIT_LOG_SECRET_KEY",
        "SIGNING_SECRET",
        "SAFETY_SALT",
        "INBOUND_EMAIL_HMAC_SECRET",
    ]:
        val = lines.get(secret_key, "")
        assert len(val) == 64, (
            f"{secret_key} should have 64 hex characters (256-bit entropy), got {len(val)}"
        )


def test_setup_wizard_idempotency(tmp_path: Path):
    """Verify setup_credentials.sh preserves existing keys upon re-run.

    @req:PRD-SYS-WIZARD-001
    """
    repo_root = Path(__file__).resolve().parents[1]
    script_path = repo_root / "scripts" / "setup_credentials.sh"
    target_env = tmp_path / ".env.idempotent"

    # Pre-populate custom key
    custom_gateway_secret = "custom-persisted-gateway-secret-abcdef123456"
    target_env.write_text(f"GATEWAY_SECRET={custom_gateway_secret}\n")

    # Run wizard in dev mode
    res = subprocess.run(
        [str(script_path), "--dev", f"--env-file={target_env}"],
        cwd=str(repo_root),
        capture_output=True,
        text=True,
        check=False,
    )
    assert res.returncode == 0
    content = target_env.read_text()
    assert f"GATEWAY_SECRET={custom_gateway_secret}" in content


def test_cli_setup_help():
    """Verify cadence setup --help displays setup subcommands.

    @req:PRD-SYS-WIZARD-001
    """
    result = runner.invoke(app, ["setup", "--help"])
    assert result.exit_code == 0
    assert "credentials" in result.output


def test_cli_setup_credentials_json(tmp_path: Path):
    """Verify cadence setup credentials --json outputs structured JSON.

    @req:PRD-SYS-WIZARD-001
    """
    target_env = tmp_path / ".env.cli_test"
    result = runner.invoke(
        app,
        [
            "--json",
            "setup",
            "credentials",
            "--dev",
            "--env-file",
            str(target_env),
        ],
    )
    assert result.exit_code == 0
    data = json.loads(result.output)
    assert data["command"] == "setup credentials"
    assert data["success"] is True
    assert data["dev_mode"] is True
    assert target_env.exists()
