import os
import subprocess


def test_db_lifecycle_safety_guard_production():
    """
    Verify that db_lifecycle.py fails when a production database connection string is set in DATABASE_URL.
    """
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
    """
    Verify that db_lifecycle.py fails when a non-local database host is set in DATABASE_URL.
    """
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


def test_db_lifecycle_success_offline():
    """
    Verify that db_lifecycle.py completes successfully with exit code 0 when connection URLs
    are local, even if network databases (Postgres/Neo4j) are offline, using in-memory SQLite databases.
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

    res = subprocess.run(
        ["uv", "run", "python", "scripts/db_lifecycle.py", "--allow-offline"],
        env=env,
        capture_output=True,
        text=True,
    )
    assert res.returncode == 0
    assert "SUCCESS" in res.stdout
