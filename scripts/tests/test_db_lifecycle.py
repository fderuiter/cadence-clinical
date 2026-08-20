import os
import subprocess
from pathlib import Path

from scripts.db_lifecycle import load_yaml_seed_config, validate_local_only


def test_validate_local_only_valid():
    """Verify validate_local_only allows standard local URLs."""
    validate_local_only(
        "Postgres", "postgresql+asyncpg://cadence:pwd@localhost:5432/db"  # pragma: allowlist secret
    )
    validate_local_only("Neo4j", "bolt://127.0.0.1:7687")
    validate_local_only("SQLite", "sqlite+aiosqlite:////app/tmf.db")


def test_load_yaml_seed_config():
    """Verify load_yaml_seed_config loads baseline YAML file and returns dictionary."""
    data = load_yaml_seed_config()
    assert isinstance(data, dict)
    assert "studies" in data
    assert "etmf" in data


def test_db_lifecycle_safety_guard_production():
    """Verify that db_lifecycle.py fails when a production keyword is set in DATABASE_URL."""
    env = os.environ.copy()
    env["DATABASE_URL"] = (
        "postgresql+asyncpg://cadence:cadence_password@prod-db.cadence.com:5432/cadence_edc"  # pragma: allowlist secret
    )

    res = subprocess.run(
        ["uv", "run", "python", "scripts/db_lifecycle.py", "--allow-offline"],
        env=env,
        capture_output=True,
        text=True,
    )
    assert res.returncode == 1
    assert (
        "Safety Guardrail Violation" in res.stderr
        or "Safety Guardrail Violation" in res.stdout
    )


def test_db_lifecycle_safety_guard_non_local():
    """Verify that db_lifecycle.py fails when a non-local host is set in DATABASE_URL."""
    env = os.environ.copy()
    env["DATABASE_URL"] = (
        "postgresql+asyncpg://cadence:cadence_password@remote-host:5432/cadence_edc"  # pragma: allowlist secret
    )

    res = subprocess.run(
        ["uv", "run", "python", "scripts/db_lifecycle.py", "--allow-offline"],
        env=env,
        capture_output=True,
        text=True,
    )
    assert res.returncode == 1
    assert (
        "Safety Guardrail Violation" in res.stderr
        or "Safety Guardrail Violation" in res.stdout
    )


def test_db_lifecycle_success_offline(tmp_path: Path):
    """
    Verify that db_lifecycle.py completes successfully with exit code 0 when connection URLs
    are local, even if network databases are offline, using in-memory databases.
    """
    env = os.environ.copy()
    env["DATABASE_URL"] = (
        "postgresql+asyncpg://cadence:cadence_password@localhost:5999/cadence_edc"  # pragma: allowlist secret
    )
    env["NEO4J_URI"] = "bolt://localhost:7687"
    env["AUDIT_LOG_SECRET_KEY"] = (
        "test-gxp-audit-secret-key-placeholder-abc"  # pragma: allowlist secret
    )
    env["INBOUND_EMAIL_HMAC_SECRET"] = "dummy"  # pragma: allowlist secret
    env["ETMF_DATABASE_URL"] = "sqlite+aiosqlite:///:memory:"
    env["CTMS_DATABASE_URL"] = "sqlite+aiosqlite:///:memory:"
    env["QUALITY_DATABASE_URL"] = "sqlite+aiosqlite:///:memory:"
    env["INTEROP_DATABASE_URL"] = "sqlite+aiosqlite:///:memory:"
    env["TICKETS_DATABASE_URL"] = "sqlite+aiosqlite:///:memory:"
    env["NOTIFICATIONS_DATABASE_URL"] = "sqlite+aiosqlite:///:memory:"
    env["ECONSENT_DATABASE_URL"] = "sqlite+aiosqlite:///:memory:"
    env["SAFETY_DATABASE_URL"] = "sqlite+aiosqlite:///:memory:"
    env["ORG_DATABASE_URL"] = "sqlite+aiosqlite:///:memory:"
    env["EISF_DATABASE_URL"] = "sqlite+aiosqlite:///:memory:"

    custom_yaml = tmp_path / "custom_seed.yaml"
    custom_yaml.write_text(
        "studies:\n  - id: custom_01\n    title: Custom Trial\netmf:\n  studies: [custom_01]\n  milestones: [INITIATION]\n"
    )

    res = subprocess.run(
        [
            "uv",
            "run",
            "python",
            "scripts/db_lifecycle.py",
            "--allow-offline",
            "--seed-file",
            str(custom_yaml),
        ],
        env=env,
        capture_output=True,
        text=True,
    )
    assert res.returncode == 0
    assert "SUCCESS" in res.stdout
