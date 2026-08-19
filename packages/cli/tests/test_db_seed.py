"""Unit and integration tests for Cadence CLI multi-engine database seeding."""

import json
import sqlite3
from pathlib import Path

from typer.testing import CliRunner

from packages.cli.main import app

runner = CliRunner()


def test_cli_db_seed_full_cadence_101_json(tmp_path: Path):
    """Verify cadence db seed --tier full --scenario CADENCE-101 outputs structured JSON with all entity summaries."""
    result = runner.invoke(
        app,
        ["--json", "db", "seed", "--tier", "full", "--scenario", "CADENCE-101"],
    )
    assert result.exit_code == 0, f"Command failed: {result.output}"
    data = json.loads(result.output)
    assert data["command"] == "seed"
    assert data["success"] is True

    seeded = data["seeded_entities"]
    assert seeded["scenario"] == "CADENCE-101"
    assert seeded["tier"] == "full"

    summary = seeded["summary"]
    assert summary.get("econsent_records", 0) >= 10
    assert summary.get("eisf_documents", 0) >= 5
    assert summary.get("safety_sae_cases", 0) >= 1
    assert summary.get("clinical_tickets", 0) >= 1
    assert summary.get("notification_dispatches", 0) >= 1
    assert summary.get("interop_messages", 0) >= 1
    assert summary.get("study_arms_count", 0) == 2
    assert summary.get("study_epochs_count", 0) == 3
    assert summary.get("study_encounters_count", 0) == 6
    assert summary.get("biomedical_concepts_count", 0) >= 12
    assert summary.get("hero_study_id") == "CADENCE-101"


def test_cli_db_seed_sqlite_content():
    """Verify that SQLite database tables contain populated CADENCE-101 records."""
    result = runner.invoke(
        app,
        ["--json", "db", "seed", "--tier", "full", "--scenario", "CADENCE-101"],
    )
    assert result.exit_code == 0

    repo_root = Path(__file__).resolve().parents[3]

    # Verify eConsent DB
    econsent_db = repo_root / "econsent.db"
    assert econsent_db.exists()
    with sqlite3.connect(econsent_db) as conn:
        count = conn.execute("SELECT COUNT(*) FROM subject_consents").fetchone()[0]
        assert count >= 10

    # Verify eISF DB
    eisf_db = repo_root / "eisf.db"
    assert eisf_db.exists()
    with sqlite3.connect(eisf_db) as conn:
        count = conn.execute("SELECT COUNT(*) FROM isf_documents").fetchone()[0]
        assert count >= 5

    # Verify Interop DB
    interop_db = repo_root / "interop.db"
    assert interop_db.exists()
    with sqlite3.connect(interop_db) as conn:
        count = conn.execute("SELECT COUNT(*) FROM interop_messages").fetchone()[0]
        assert count >= 1


def test_cli_db_seed_tier_filtering():
    """Verify tier filtering only seeds the requested tier domain."""
    result_proto = runner.invoke(
        app,
        ["--json", "db", "seed", "--tier", "protocol", "--scenario", "CADENCE-101"],
    )
    assert result_proto.exit_code == 0
    data_proto = json.loads(result_proto.output)
    assert data_proto["seeded_entities"]["tier"] == "protocol"
    assert "study_arms_count" in data_proto["seeded_entities"]["summary"]
