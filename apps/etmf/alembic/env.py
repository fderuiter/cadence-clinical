import asyncio
import os
import sys
from logging.config import fileConfig

from alembic import context
from sqlalchemy import inspect, pool, text
from sqlalchemy.ext.asyncio import async_engine_from_config

# 1. Interpret the config file for Python logging.
config = context.config
if config.config_file_name is not None:
    fileConfig(config.config_file_name)

# 2. Add path to sys.path so we can import apps
sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "../../..")))

# 3. Import Base models metadata
from apps.etmf.adapters.models import Base  # noqa: E402

target_metadata = Base.metadata

# 4. Read connection URL from environment or fallback
db_url = os.getenv("ETMF_DATABASE_URL", "sqlite+aiosqlite:///:memory:")
config.set_main_option("sqlalchemy.url", db_url)


def run_migrations_offline() -> None:
    """Run migrations in 'offline' mode."""
    url = config.get_main_option("sqlalchemy.url")
    context.configure(
        url=url,
        target_metadata=target_metadata,
        literal_binds=True,
        dialect_opts={"paramstyle": "named"},
    )

    with context.begin_transaction():
        context.run_migrations()


async def deploy_triggers(connection) -> None:
    dialect_name = connection.dialect.name

    def has_table(sync_conn):
        return inspect(sync_conn).has_table("tmf_document_qc_transitions")

    table_exists = await connection.run_sync(has_table)
    if not table_exists:
        return

    try:
        if dialect_name == "postgresql":
            await connection.execute(
                text("""
                CREATE OR REPLACE FUNCTION block_qc_transition_mutation()
                RETURNS TRIGGER AS $$
                BEGIN
                    RAISE EXCEPTION 'IMMUTABILITY_VIOLATION: DocumentQCTransition records are append-only.';
                END;
                $$ LANGUAGE plpgsql;
            """)
            )
            await connection.execute(
                text(
                    "DROP TRIGGER IF EXISTS tmf_document_qc_transitions_no_update ON tmf_document_qc_transitions;"
                )
            )
            await connection.execute(
                text("""
                CREATE TRIGGER tmf_document_qc_transitions_no_update
                BEFORE UPDATE ON tmf_document_qc_transitions
                FOR EACH ROW EXECUTE FUNCTION block_qc_transition_mutation();
            """)
            )
            await connection.execute(
                text(
                    "DROP TRIGGER IF EXISTS tmf_document_qc_transitions_no_delete ON tmf_document_qc_transitions;"
                )
            )
            await connection.execute(
                text("""
                CREATE TRIGGER tmf_document_qc_transitions_no_delete
                BEFORE DELETE ON tmf_document_qc_transitions
                FOR EACH ROW EXECUTE FUNCTION block_qc_transition_mutation();
            """)
            )
        elif dialect_name == "sqlite":
            await connection.execute(
                text("""
                CREATE TRIGGER IF NOT EXISTS tmf_document_qc_transitions_no_update
                BEFORE UPDATE ON tmf_document_qc_transitions
                BEGIN
                    SELECT RAISE(FAIL, 'IMMUTABILITY_VIOLATION: DocumentQCTransition records are append-only and cannot be updated.');
                END;
            """)
            )
            await connection.execute(
                text("""
                CREATE TRIGGER IF NOT EXISTS tmf_document_qc_transitions_no_delete
                BEFORE DELETE ON tmf_document_qc_transitions
                BEGIN
                    SELECT RAISE(FAIL, 'IMMUTABILITY_VIOLATION: DocumentQCTransition records are append-only and cannot be deleted.');
                END;
            """)
            )
    except Exception:
        pass


def do_run_migrations(connection) -> None:
    context.configure(connection=connection, target_metadata=target_metadata)

    with context.begin_transaction():
        context.run_migrations()


async def run_migrations_online() -> None:
    """Run migrations in 'online' mode."""
    connectable = async_engine_from_config(
        config.get_section(config.config_ini_section, {}),
        prefix="sqlalchemy.",
        poolclass=pool.NullPool,
    )

    async with connectable.connect() as connection:
        await connection.run_sync(do_run_migrations)
        await deploy_triggers(connection)
        await connection.commit()

    await connectable.dispose()


if context.is_offline_mode():
    run_migrations_offline()
else:
    asyncio.run(run_migrations_online())
