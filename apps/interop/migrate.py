import argparse
import asyncio
import os
import sys

from sqlalchemy.ext.asyncio import create_async_engine
from apps.interop.models import Base


async def run_migrations(database_url: str) -> None:
    """
    Execute asynchronous pre-boot schema migrations for Interop.
    """
    print(f"Starting pre-boot schema migration for Interop database {database_url}...")
    engine_options = {}
    engine = create_async_engine(database_url, echo=False, **engine_options)
    try:
        async with engine.begin() as conn:
            # Setup metadata tables
            await conn.run_sync(Base.metadata.create_all)
        print("Interop Schema migration completed successfully.")
    except Exception as e:
        print(f"Interop Schema migration failed: {e}")
        sys.exit(1)
    finally:
        await engine.dispose()


def main() -> None:
    """
    Entry point for the Interop pre-boot migration runner script.
    """
    parser = argparse.ArgumentParser(
        description="Pre-boot Database Schema Migration Runner for Interop"
    )
    parser.add_argument(
        "--db-url",
        type=str,
        default=os.getenv("INTEROP_DATABASE_URL", "sqlite+aiosqlite:///:memory:"),
        help="Database URL for migration",
    )
    args = parser.parse_args()

    asyncio.run(run_migrations(args.db_url))


if __name__ == "__main__":
    main()
