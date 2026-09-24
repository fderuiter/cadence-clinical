"""Unit tests for the CLI reference documentation generator (scripts/generate_cli_docs.py)."""

import sys
from pathlib import Path
import pytest

REPO_ROOT = Path(__file__).resolve().parents[3]
if str(REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(REPO_ROOT))

from scripts.generate_cli_docs import generate_markdown, main
from packages.cli.mcp.server import CadenceMcpServer


def test_generate_cli_docs_structure():
    """Verify that generated markdown includes all required sections and formatting."""
    md = generate_markdown()

    # Verify global options
    assert "# Cadence Unified Developer CLI Reference" in md
    assert "## Global Options" in md
    assert "`--json`" in md
    assert "`--verbose`" in md

    # Verify all 12 command groups
    expected_commands = [
        "doctor",
        "dev",
        "test",
        "check",
        "fix",
        "db",
        "scaffold",
        "setup",
        "record",
        "gxp",
        "cdisc",
        "mcp",
    ]
    for cmd in expected_commands:
        assert f"### `cadence {cmd}`" in md

    # Verify phantom flags are purged from dev
    dev_section = md.split("### `cadence dev`")[1].split("### `cadence test`")[0]
    assert "--daemon" not in dev_section
    assert "--stop" not in dev_section
    assert "`--services`" not in dev_section

    # Verify MCP tools
    assert "#### Native JSON-RPC MCP Tools" in md
    for tool in CadenceMcpServer.TOOLS:
        assert f"##### `{tool['name']}`" in md

    # Verify JSON-RPC protocol section
    assert "##### JSON-RPC Stdio Protocol Specification" in md


def test_cli_docs_file_in_sync():
    """Verify that docs/cli.md is strictly up-to-date with current CLI implementation."""
    cli_docs_path = REPO_ROOT / "docs" / "cli.md"
    assert cli_docs_path.exists(), "docs/cli.md must exist"

    existing_content = cli_docs_path.read_text(encoding="utf-8").strip()
    generated_content = generate_markdown().strip()

    assert existing_content == generated_content, (
        "docs/cli.md is out of sync with Typer CLI definition in packages/cli/main.py. "
        "Run 'uv run python scripts/generate_cli_docs.py' or 'uv run cadence fix' to regenerate."
    )


def test_generate_cli_docs_check_flag(capsys, monkeypatch):
    """Verify scripts/generate_cli_docs.py --check exits 0 when docs/cli.md is up-to-date."""
    cli_docs_path = REPO_ROOT / "docs" / "cli.md"
    monkeypatch.setattr("sys.argv", ["generate_cli_docs.py", "--check", "--output", str(cli_docs_path)])

    with pytest.raises(SystemExit) as exc_info:
        main()

    assert exc_info.value.code == 0
    captured = capsys.readouterr()
    assert "is up-to-date" in captured.out


def test_generate_cli_docs_drift_detection(tmp_path, capsys, monkeypatch):
    """Verify scripts/generate_cli_docs.py --check exits 1 when drift is detected."""
    stale_file = tmp_path / "cli.md"
    stale_file.write_text("# Outdated CLI Reference\n", encoding="utf-8")

    monkeypatch.setattr("sys.argv", ["generate_cli_docs.py", "--check", "--output", str(stale_file)])

    with pytest.raises(SystemExit) as exc_info:
        main()

    assert exc_info.value.code == 1
    captured = capsys.readouterr()
    assert "CLI documentation drift detected" in captured.out
