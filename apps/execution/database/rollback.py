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

from sqlalchemy import inspect, text
from sqlalchemy.ext.asyncio import create_async_engine

from apps.execution.database.migrate import run_migrations


async def validate_migration_integrity() -> bool:
    # Use a temporary file-backed sqlite database to avoid transient memory disposal
    temp_dir = tempfile.gettempdir()
    db_file = os.path.join(temp_dir, f"test_rollback_{uuid.uuid4().hex}.db")
    db_url = f"sqlite+aiosqlite:///{db_file}"

    print(f"[Migration-Integrity] Starting integrity validation on ephemeral: {db_url}")
    engine = create_async_engine(db_url, echo=False)

    try:
        # Step 1: Run Forward Migrations
        print("[Migration-Integrity] Step 1: Running forward migrations...")
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

            # Step 3: Simulate Rollback to an earlier version (dropping newly added column)
            print("[Migration-Integrity] Step 3: Executing simulated rollback...")

            def get_cols(sync_conn, table):
                insp = inspect(sync_conn)
                return [col["name"] for col in insp.get_columns(table)]

            cols_before = await conn.run_sync(
                lambda sc: get_cols(sc, "clinical_observations")
            )
            if "page_id" not in cols_before:
                print(
                    "[Migration-Integrity] Error: 'page_id' column was not added by migration.",
                    file=sys.stderr,
                )
                return False

            # Drop triggers referencing the column before dropping the column in SQLite
            try:
                await conn.execute(
                    text(
                        "DROP TRIGGER IF EXISTS trg_audit_clinical_observations_insert;"
                    )
                )
                await conn.execute(
                    text(
                        "DROP TRIGGER IF EXISTS trg_audit_clinical_observations_update;"
                    )
                )
                await conn.execute(
                    text(
                        "DROP TRIGGER IF EXISTS trg_audit_clinical_observations_delete;"
                    )
                )
                print(
                    "[Migration-Integrity] Dropped table triggers referencing page_id."
                )
            except Exception as e:
                print(f"[Migration-Integrity] Warning during trigger drop: {e}")

            # Now drop 'page_id' to simulate rollback
            try:
                await conn.execute(
                    text("ALTER TABLE clinical_observations DROP COLUMN page_id;")
                )
                print(
                    "[Migration-Integrity] Dropped column 'page_id' successfully (simulated rollback)."
                )
            except Exception as e:
                print(f"[Migration-Integrity] Rollback column drop failed: {e}")
                return False

            # Validate that page_id column is indeed gone
            cols_after = await conn.run_sync(
                lambda sc: get_cols(sc, "clinical_observations")
            )
            if "page_id" in cols_after:
                print(
                    "[Migration-Integrity] Error: Rollback failed, 'page_id' is still present.",
                    file=sys.stderr,
                )
                return False

        # Step 4: Re-run upgrade/forward migration to ensure re-migration is safe and idempotent
        print(
            "[Migration-Integrity] Step 4: Testing forward re-migration idempotency..."
        )
        await run_migrations(db_url)

        async with engine.begin() as conn:
            cols_re_migrated = await conn.run_sync(
                lambda sc: get_cols(sc, "clinical_observations")
            )
            if "page_id" not in cols_re_migrated:
                print(
                    "[Migration-Integrity] Error: Re-migration failed to restore 'page_id'.",
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
