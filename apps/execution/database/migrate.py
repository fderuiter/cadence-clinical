import argparse
import asyncio
import os
import sys

from sqlalchemy.ext.asyncio import create_async_engine

from apps.execution.database.models import Base


async def run_migrations(database_url: str) -> None:
    """
    Execute asynchronous pre-boot schema migrations.

    This function sets up the database schema safely before the main web application
    starts, preventing race conditions or downtime associated with runtime migrations.

    Args:
        database_url (str): The connection string for the database to migrate.
    """
    print(f"Starting pre-boot schema migration for {database_url}...")
    engine = create_async_engine(database_url, echo=False)
    try:
        async with engine.begin() as conn:
            # We would normally use alembic or similar here, but for this setup we just create_all
            await conn.run_sync(Base.metadata.create_all)
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
