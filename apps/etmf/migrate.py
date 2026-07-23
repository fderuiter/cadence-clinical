import argparse
import asyncio
import os
import sys

from sqlalchemy.ext.asyncio import create_async_engine
from apps.etmf.models import Base


async def run_migrations(database_url: str) -> None:
    """
    Execute asynchronous pre-boot schema migrations for eTMF.
    """
    print(f"Starting pre-boot schema migration for ETMF database {database_url}...")
    engine_options = {}
    engine = create_async_engine(database_url, echo=False, **engine_options)
    try:
        async with engine.begin() as conn:
            # Setup metadata tables
            await conn.run_sync(Base.metadata.create_all)
        print("ETMF Schema migration completed successfully.")
    except Exception as e:
        print(f"ETMF Schema migration failed: {e}")
        sys.exit(1)
    finally:
        await engine.dispose()


def main() -> None:
    """
    Entry point for the ETMF pre-boot migration runner script.
    """
    parser = argparse.ArgumentParser(
        description="Pre-boot Database Schema Migration Runner for ETMF"
    )
    parser.add_argument(
        "--db-url",
        type=str,
        default=os.getenv("ETMF_DATABASE_URL", "sqlite+aiosqlite:////app/tmf.db"),
        help="Database URL for migration",
    )
    args = parser.parse_args()

    asyncio.run(run_migrations(args.db_url))


if __name__ == "__main__":
    main()
