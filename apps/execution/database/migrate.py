import argparse
import asyncio
import os
import sys

from sqlalchemy import text
from sqlalchemy.ext.asyncio import create_async_engine

from apps.execution.database.models import (  # noqa: F401
    Base,
    ClinicalObservation,
    SDVSignOff,
    TSDVConfig,
)


async def deploy_database_triggers(conn, dialect_name: str) -> None:
    """
    Deploys native database-level write-protection and mutation capture triggers.
    Supports both PostgreSQL (using PL/pgSQL) and SQLite (using native triggers and custom functions).
    """
    # 1. Prevent updates or deletes on audit logs and seals
    if dialect_name == "postgresql":
        # Create schema functions and triggers
        await conn.execute(
            text("""
            CREATE OR REPLACE FUNCTION audit_schema.prevent_audit_mutation()
            RETURNS TRIGGER AS $$
            BEGIN
                -- If it is a DELETE, always block
                IF (TG_OP = 'DELETE') THEN
                    RAISE EXCEPTION 'GxP Compliance Violation: Modification or deletion of audit logs is strictly prohibited.';
                END IF;

                -- If it is an UPDATE, check if any column other than cryptographic_seal has changed
                IF (TG_OP = 'UPDATE') THEN
                    IF (OLD.id IS DISTINCT FROM NEW.id OR
                        OLD.table_name IS DISTINCT FROM NEW.table_name OR
                        OLD.record_id IS DISTINCT FROM NEW.record_id OR
                        OLD.action IS DISTINCT FROM NEW.action OR
                        OLD.user_id IS DISTINCT FROM NEW.user_id OR
                        OLD.timestamp IS DISTINCT FROM NEW.timestamp OR
                        OLD.old_values::jsonb IS DISTINCT FROM NEW.old_values::jsonb OR
                        OLD.new_values::jsonb IS DISTINCT FROM NEW.new_values::jsonb OR
                        OLD.version_index IS DISTINCT FROM NEW.version_index OR
                        OLD.change_reason IS DISTINCT FROM NEW.change_reason) THEN
                        RAISE EXCEPTION 'GxP Compliance Violation: Modification or deletion of audit logs is strictly prohibited.';
                    END IF;
                    -- Also ensure cryptographic_seal can only transition from NULL to a value
                    IF (OLD.cryptographic_seal IS NOT NULL AND OLD.cryptographic_seal IS DISTINCT FROM NEW.cryptographic_seal) THEN
                        RAISE EXCEPTION 'GxP Compliance Violation: Modification of previously sealed audit logs is strictly prohibited.';
                    END IF;
                END IF;

                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;
        """)
        )

        await conn.execute(
            text("""
            CREATE OR REPLACE FUNCTION audit_schema.prevent_seal_mutation()
            RETURNS TRIGGER AS $$
            BEGIN
                RAISE EXCEPTION 'GxP Compliance Violation: Modification or deletion of audit logs is strictly prohibited.';
            END;
            $$ LANGUAGE plpgsql;
        """)
        )

        await conn.execute(
            text(
                "DROP TRIGGER IF EXISTS trg_lock_audit_trail_logs ON audit_schema.audit_logs;"
            )
        )
        await conn.execute(
            text("""
            CREATE TRIGGER trg_lock_audit_trail_logs
            BEFORE UPDATE OR DELETE ON audit_schema.audit_logs
            FOR EACH ROW EXECUTE FUNCTION audit_schema.prevent_audit_mutation();
        """)
        )

        await conn.execute(
            text(
                "DROP TRIGGER IF EXISTS trg_lock_audit_trail_seals ON audit_schema.audit_ledger_seals;"
            )
        )
        await conn.execute(
            text("""
            CREATE TRIGGER trg_lock_audit_trail_seals
            BEFORE UPDATE OR DELETE ON audit_schema.audit_ledger_seals
            FOR EACH ROW EXECUTE FUNCTION audit_schema.prevent_seal_mutation();
        """)
        )

        # Deploy model mutation function
        await conn.execute(
            text("""
            CREATE OR REPLACE FUNCTION public.capture_model_mutation()
            RETURNS TRIGGER AS $$
            DECLARE
                v_user_id VARCHAR(255);
                v_change_reason VARCHAR(255);
                v_new_version INTEGER := 1;
                v_old_json JSONB := NULL;
                v_new_json JSONB := NULL;
                v_action VARCHAR(50);
                v_record_id VARCHAR(255);
                v_app_writing VARCHAR(50);
            BEGIN
                IF (TG_OP = 'DELETE') THEN
                    RAISE EXCEPTION 'GxP Compliance Violation: Hard deletions are strictly forbidden for clinical entities. Use soft deletes by updating is_deleted=True.';
                    RETURN NULL;
                END IF;

                v_app_writing := COALESCE(current_setting('cadence.app_writing', true), 'false');
                IF (v_app_writing = 'true') THEN
                    RETURN NEW;
                END IF;

                v_user_id := COALESCE(current_setting('cadence.current_user_id', true), 'system_process');
                v_change_reason := COALESCE(current_setting('cadence.current_change_reason', true), 'Automated system operation');

                IF (TG_OP = 'INSERT') THEN
                    v_action := 'INSERT';
                    v_new_json := to_jsonb(NEW) - 'id';
                    v_record_id := NEW.id::VARCHAR;
                    IF (NEW.version IS NOT NULL) THEN
                        v_new_version := NEW.version;
                    END IF;
                ELSIF (TG_OP = 'UPDATE') THEN
                    v_action := 'UPDATE';
                    v_old_json := to_jsonb(OLD) - 'id';
                    v_new_json := to_jsonb(NEW) - 'id';
                    v_record_id := NEW.id::VARCHAR;

                    IF (NEW.version IS NOT NULL AND NEW.version <= OLD.version) THEN
                        NEW.version := OLD.version + 1;
                    END IF;
                    v_new_version := NEW.version;

                    IF (NEW.is_deleted IS TRUE AND OLD.is_deleted IS FALSE) THEN
                        v_action := 'DELETE';
                    END IF;
                END IF;

                INSERT INTO audit_schema.audit_logs (
                    id, table_name, record_id, action, user_id, timestamp, old_values, new_values, version_index, change_reason
                ) VALUES (
                    gen_random_uuid()::VARCHAR,
                    TG_TABLE_NAME,
                    v_record_id,
                    v_action,
                    v_user_id,
                    TIMEZONE('utc', NOW()),
                    v_old_json,
                    v_new_json,
                    v_new_version,
                    v_change_reason
                );

                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;
        """)
        )

    elif dialect_name == "sqlite":
        # Create write protection triggers on SQLite tables
        # Use IS NOT NULL/IS NOT to detect if any fields other than cryptographic_seal are modified.
        await conn.execute(
            text("""
            CREATE TRIGGER IF NOT EXISTS trg_lock_audit_trail_logs_update
            BEFORE UPDATE ON audit_logs
            WHEN (
                NEW.id IS NOT OLD.id OR
                NEW.table_name IS NOT OLD.table_name OR
                NEW.record_id IS NOT OLD.record_id OR
                NEW.action IS NOT OLD.action OR
                NEW.user_id IS NOT OLD.user_id OR
                NEW.timestamp IS NOT OLD.timestamp OR
                NEW.old_values IS NOT OLD.old_values OR
                NEW.new_values IS NOT OLD.new_values OR
                NEW.version_index IS NOT OLD.version_index OR
                NEW.change_reason IS NOT OLD.change_reason OR
                (OLD.cryptographic_seal IS NOT NULL AND NEW.cryptographic_seal IS NOT OLD.cryptographic_seal)
            )
            BEGIN
                SELECT RAISE(FAIL, 'GxP Compliance Violation: Modification or deletion of audit logs is strictly prohibited.');
            END;
        """)
        )
        await conn.execute(
            text("""
            CREATE TRIGGER IF NOT EXISTS trg_lock_audit_trail_logs_delete
            BEFORE DELETE ON audit_logs
            BEGIN
                SELECT RAISE(FAIL, 'GxP Compliance Violation: Modification or deletion of audit logs is strictly prohibited.');
            END;
        """)
        )
        await conn.execute(
            text("""
            CREATE TRIGGER IF NOT EXISTS trg_lock_audit_trail_seals_update
            BEFORE UPDATE ON audit_ledger_seals
            BEGIN
                SELECT RAISE(FAIL, 'GxP Compliance Violation: Modification or deletion of audit logs is strictly prohibited.');
            END;
        """)
        )
        await conn.execute(
            text("""
            CREATE TRIGGER IF NOT EXISTS trg_lock_audit_trail_seals_delete
            BEFORE DELETE ON audit_ledger_seals
            BEGIN
                SELECT RAISE(FAIL, 'GxP Compliance Violation: Modification or deletion of audit logs is strictly prohibited.');
            END;
        """)
        )

    # 2. Iterate through all tables in metadata and deploy audited model triggers if columns are present
    for table_name, table in Base.metadata.tables.items():
        if "version" in table.columns and "is_deleted" in table.columns:
            if dialect_name == "postgresql":
                await conn.execute(
                    text(
                        f"DROP TRIGGER IF EXISTS trg_audit_{table_name} ON {table_name};"
                    )
                )
                await conn.execute(
                    text(f"""
                    CREATE TRIGGER trg_audit_{table_name}
                    BEFORE INSERT OR UPDATE OR DELETE ON {table_name}
                    FOR EACH ROW EXECUTE FUNCTION public.capture_model_mutation();
                """)
                )
            elif dialect_name == "sqlite":
                # For SQLite, we define dynamic triggers using JSON object serialization.
                new_cols = []
                old_cols = []
                for col in table.columns:
                    if col.name != "id":
                        new_cols.append(f"'{col.name}'")
                        new_cols.append(f"NEW.{col.name}")
                        old_cols.append(f"'{col.name}'")
                        old_cols.append(f"OLD.{col.name}")
                new_fields_sql = ", ".join(new_cols)
                old_fields_sql = ", ".join(old_cols)

                # Drop triggers to recreate them
                await conn.execute(
                    text(f"DROP TRIGGER IF EXISTS trg_audit_{table_name}_insert;")
                )
                await conn.execute(
                    text(f"DROP TRIGGER IF EXISTS trg_audit_{table_name}_update;")
                )
                await conn.execute(
                    text(f"DROP TRIGGER IF EXISTS trg_audit_{table_name}_delete;")
                )

                await conn.execute(
                    text(f"""
                    CREATE TRIGGER trg_audit_{table_name}_insert
                    AFTER INSERT ON {table_name}
                    WHEN (current_setting('cadence.app_writing', 1) <> 'true')
                    BEGIN
                        INSERT INTO audit_logs (
                            id, table_name, record_id, action, user_id, timestamp, old_values, new_values, version_index, change_reason
                        ) VALUES (
                            gen_random_uuid(),
                            '{table_name}',
                            NEW.id,
                            'INSERT',
                            current_setting('cadence.current_user_id', 1),
                            datetime('now'),
                            NULL,
                            json_object({new_fields_sql}),
                            coalesce(NEW.version, 1),
                            current_setting('cadence.current_change_reason', 1)
                        );
                    END;
                """)
                )

                await conn.execute(
                    text(f"""
                    CREATE TRIGGER trg_audit_{table_name}_update
                    AFTER UPDATE ON {table_name}
                    WHEN (current_setting('cadence.app_writing', 1) <> 'true')
                    BEGIN
                        INSERT INTO audit_logs (
                            id, table_name, record_id, action, user_id, timestamp, old_values, new_values, version_index, change_reason
                        ) VALUES (
                            gen_random_uuid(),
                            '{table_name}',
                            NEW.id,
                            CASE WHEN NEW.is_deleted = 1 AND OLD.is_deleted = 0 THEN 'DELETE' ELSE 'UPDATE' END,
                            current_setting('cadence.current_user_id', 1),
                            datetime('now'),
                            json_object({old_fields_sql}),
                            json_object({new_fields_sql}),
                            coalesce(NEW.version, 1),
                            current_setting('cadence.current_change_reason', 1)
                        );
                    END;
                """)
                )

                await conn.execute(
                    text(f"""
                    CREATE TRIGGER trg_audit_{table_name}_delete
                    BEFORE DELETE ON {table_name}
                    BEGIN
                        SELECT RAISE(FAIL, 'GxP Compliance Violation: Hard deletions are strictly forbidden for clinical entities. Use soft deletes by updating is_deleted=True.');
                    END;
                """)
                )


async def run_migrations(database_url: str) -> None:
    """
    Execute asynchronous pre-boot schema migrations.

    This function sets up the database schema safely before the main web application
    starts, preventing race conditions or downtime associated with runtime migrations.

    Args:
        database_url (str): The connection string for the database to migrate.
    """
    print(f"Starting pre-boot schema migration for {database_url}...")
    engine_options = {}
    if database_url.startswith("sqlite"):
        engine_options["execution_options"] = {
            "schema_translate_map": {"audit_schema": None}
        }

    engine = create_async_engine(database_url, echo=False, **engine_options)
    try:
        async with engine.begin() as conn:
            if engine.dialect.name == "postgresql":
                await conn.execute(text("CREATE SCHEMA IF NOT EXISTS audit_schema;"))

            # Setup metadata tables
            await conn.run_sync(Base.metadata.create_all)

            # Deploy database triggers
            await deploy_database_triggers(conn, engine.dialect.name)

        print("Schema migration completed successfully.")
    except Exception as e:
        print(f"Schema migration failed: {e}")
        sys.exit(1)
    finally:
        await engine.dispose()


def main() -> None:
    """
    Entry point for the pre-boot migration runner script.

    Parses CLI arguments for the database URL and executes the migration routine.
    """
    parser = argparse.ArgumentParser(
        description="Pre-boot Database Schema Migration Runner"
    )
    parser.add_argument(
        "--db-url",
        type=str,
        default=os.getenv("DATABASE_URL", "sqlite+aiosqlite:///:memory:"),
        help="Database URL for migration",
    )
    args = parser.parse_args()

    asyncio.run(run_migrations(args.db_url))


if __name__ == "__main__":
    main()
