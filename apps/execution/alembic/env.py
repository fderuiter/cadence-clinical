import asyncio
import os
import sys
from logging.config import fileConfig

from alembic import context
from sqlalchemy import pool, text, inspect
from sqlalchemy.ext.asyncio import async_engine_from_config

# 1. Interpret the config file for Python logging.
config = context.config
if config.config_file_name is not None:
    fileConfig(config.config_file_name)

# 2. Add path to sys.path so we can import apps
sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "../../..")))

# 3. Import Base models metadata. Import Base triggers discover_models() programmatically.
from apps.execution.database.models import Base  # noqa: E402

target_metadata = Base.metadata

# 4. Read connection URL from environment or fallback
db_url = os.getenv("EXECUTION_DATABASE_URL", os.getenv("DATABASE_URL", "sqlite+aiosqlite:///:memory:"))
config.set_main_option("sqlalchemy.url", db_url)


def run_migrations_offline() -> None:
    """Run migrations in 'offline' mode."""
    url = config.get_main_option("sqlalchemy.url")
    context_args = {}
    if url and url.startswith("sqlite"):
        context_args["schema_translate_map"] = {"audit_schema": None}
    context.configure(
        url=url,
        target_metadata=target_metadata,
        literal_binds=True,
        dialect_opts={"paramstyle": "named"},
        **context_args
    )

    with context.begin_transaction():
        context.run_migrations()


def do_run_migrations(connection) -> None:
    # Set up sqlite schema translate map if needed
    context_args = {}
    if connection.dialect.name == "sqlite":
        context_args["schema_translate_map"] = {"audit_schema": None}
    context.configure(
        connection=connection,
        target_metadata=target_metadata,
        render_as_batch=True,  # SQLite safe alterations
        **context_args
    )

    with context.begin_transaction():
        context.run_migrations()


async def run_migrations_online() -> None:
    """Run migrations in 'online' mode."""
    # We can pass dialect-specific options or custom configurations here
    connectable = async_engine_from_config(
        config.get_section(config.config_ini_section, {}),
        prefix="sqlalchemy.",
        poolclass=pool.NullPool,
    )

    async with connectable.connect() as connection:
        if connectable.dialect.name == "sqlite":
            connection = await connection.execution_options(
                schema_translate_map={"audit_schema": None}
            )
        # Run standard schema migrations
        await connection.run_sync(do_run_migrations)
        
        # Deploy native write-protection and GxP triggers only if the target tables are already created
        has_audit_logs = await connection.run_sync(
            lambda sc: inspect(sc).has_table("audit_logs", schema=None)
        )
        if has_audit_logs:
            from apps.execution.database.migrate import deploy_database_triggers
            await deploy_database_triggers(connection, connectable.dialect.name)
        
        await connection.commit()

    await connectable.dispose()


if context.is_offline_mode():
    run_migrations_offline()
else:
    asyncio.run(run_migrations_online())
