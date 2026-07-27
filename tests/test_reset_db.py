import os
import subprocess


def test_reset_db_safety_guard_production():
    """
    Verify that reset_db.py fails when a production database connection string is set in DATABASE_URL.
    """
    env = os.environ.copy()
    env["DATABASE_URL"] = (
        "postgresql+asyncpg://cadence:cadence_password@prod-db.cadence.com:5432/cadence_edc"  # pragma: allowlist secret
    )

    res = subprocess.run(
        ["uv", "run", "python", "scripts/reset_db.py", "--allow-offline"],
        env=env,
        capture_output=True,
        text=True,
    )
    assert res.returncode == 1
    assert (
        "Safety Guardrail Violation" in res.stderr
        or "Safety Guardrail Violation" in res.stdout
    )


def test_reset_db_safety_guard_non_local():
    """
    Verify that reset_db.py fails when a non-local database host is set in DATABASE_URL.
    """
    env = os.environ.copy()
    env["DATABASE_URL"] = (
        "postgresql+asyncpg://cadence:cadence_password@remote-host:5432/cadence_edc"  # pragma: allowlist secret
    )

    res = subprocess.run(
        ["uv", "run", "python", "scripts/reset_db.py", "--allow-offline"],
        env=env,
        capture_output=True,
        text=True,
    )
    assert res.returncode == 1
    assert (
        "Safety Guardrail Violation" in res.stderr
        or "Safety Guardrail Violation" in res.stdout
    )


def test_reset_db_success_offline():
    """
    Verify that reset_db.py completes successfully with exit code 0 when connection URLs
    are local, even if network databases (Postgres/Neo4j) are offline, by using in-memory databases
    for SQLite testing context.
    """
    env = os.environ.copy()
    # Set all connection strings to local/in-memory to ensure safety and speed
    env["DATABASE_URL"] = (
        "postgresql+asyncpg://cadence:cadence_password@localhost:5432/cadence_edc"  # pragma: allowlist secret
    )
    env["NEO4J_URI"] = "bolt://localhost:7687"
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

    res = subprocess.run(
        ["uv", "run", "python", "scripts/reset_db.py", "--allow-offline"],
        env=env,
        capture_output=True,
        text=True,
    )
    assert res.returncode == 0
    assert "SUCCESS" in res.stdout
