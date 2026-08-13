#!/usr/bin/env python3
"""
Database Migration Rollback and Schema Integrity Validator.
Executes forward migration, performs a simulated rollback, and verifies schema integrity.
"""

import asyncio
import os
import sys
import tempfile
import uuid

# Set fail-fast required cryptographic environment variables for non-production validation/CLI import
os.environ.setdefault(
    "AUDIT_LOG_SECRET_KEY", "test-gxp-audit-secret-key-placeholder-abc"
)
os.environ.setdefault(
    "INBOUND_EMAIL_HMAC_SECRET", "test-email-hmac-secret-placeholder-xyz"
)
os.environ.setdefault("GATEWAY_SECRET", "internal-gateway-secret-12345")
os.environ.setdefault("SIGNING_SECRET", "designer-amendment-secure-key-12345")

from sqlalchemy import inspect
from sqlalchemy.ext.asyncio import create_async_engine

from apps.execution.database.migrate import run_migrations


async def run_downgrade(database_url: str) -> None:
    print(f"[Migration-Integrity] Rolling back database to base schema: {database_url}")
    env = os.environ.copy()
    env["EXECUTION_DATABASE_URL"] = database_url
    cmd = [
        sys.executable,
        "-m",
        "alembic",
        "-c",
        "apps/execution/alembic.ini",
        "downgrade",
        "base",
    ]
    process = await asyncio.create_subprocess_exec(
        *cmd, env=env, stdout=asyncio.subprocess.PIPE, stderr=asyncio.subprocess.PIPE
    )
    stdout, stderr = await process.communicate()
    if process.returncode != 0:
        raise RuntimeError(f"Alembic downgrade failed for execution: {stderr.decode()}")


async def validate_migration_integrity() -> bool:
    # Use a temporary file-backed sqlite database to avoid transient memory disposal
    temp_dir = tempfile.gettempdir()
    db_file = os.path.join(temp_dir, f"test_rollback_{uuid.uuid4().hex}.db")
    db_url = f"sqlite+aiosqlite:///{db_file}"

    print(f"[Migration-Integrity] Starting integrity validation on ephemeral: {db_url}")
    engine = create_async_engine(db_url, echo=False)

    try:
        # Step 1: Run Forward Migrations
        print("[Migration-Integrity] Step 1: Running forward migrations to head...")
        await run_migrations(db_url)

        # Step 2: Verify tables are created and healthy
        print("[Migration-Integrity] Step 2: Verifying table creation...")
        async with engine.begin() as conn:

            def get_tables(sync_conn):
                insp = inspect(sync_conn)
                return insp.get_table_names()

            tables = await conn.run_sync(get_tables)
            print(f"[Migration-Integrity] Created tables: {tables}")
            if "clinical_observations" not in tables or "audit_logs" not in tables:
                print(
                    "[Migration-Integrity] Error: Missing expected tables after migration.",
                    file=sys.stderr,
                )
                return False

        # Step 3: Run Downgrade base to drop everything
        print(
            "[Migration-Integrity] Step 3: Running standard Alembic rollback to base..."
        )
        await run_downgrade(db_url)

        # Verify that all tables have been dropped
        async with engine.begin() as conn:
            tables_after_rollback = await conn.run_sync(get_tables)
            print(
                f"[Migration-Integrity] Tables after rollback: {tables_after_rollback}"
            )
            # Ensure alembic_version table may remain or is empty, but clinical tables are dropped
            if "clinical_observations" in tables_after_rollback:
                print(
                    "[Migration-Integrity] Error: clinical_observations table still exists after rollback.",
                    file=sys.stderr,
                )
                return False

        # Step 4: Re-run upgrade/forward migration to ensure re-migration is safe and idempotent
        print(
            "[Migration-Integrity] Step 4: Testing forward re-migration idempotency..."
        )
        await run_migrations(db_url)

        async with engine.begin() as conn:
            tables_re_migrated = await conn.run_sync(get_tables)
            if "clinical_observations" not in tables_re_migrated:
                print(
                    "[Migration-Integrity] Error: Re-migration failed to restore tables.",
                    file=sys.stderr,
                )
                return False

        print(
            "[Migration-Integrity] Migration Rollback/Forward Integrity validation completed successfully."
        )
        return True

    except Exception as e:
        print(
            f"[Migration-Integrity] Exception during migration validation: {e}",
            file=sys.stderr,
        )
        return False
    finally:
        await engine.dispose()
        if os.path.exists(db_file):
            try:
                os.remove(db_file)
            except Exception as e:
                print(f"[Migration-Integrity] Error removing temp file {db_file}: {e}")


def main():
    success = asyncio.run(validate_migration_integrity())
    if not success:
        print(
            "[Migration-Integrity] ERROR: Migration Rollback/Forward Integrity validation FAILED.",
            file=sys.stderr,
        )
        sys.exit(1)


if __name__ == "__main__":
    main()
