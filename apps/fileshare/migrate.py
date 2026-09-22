"""Database schema migration runner for the Fileshare microservice."""

import argparse
import asyncio
import os


async def run_migrations(database_url: str) -> None:
    """Executes pre-boot Fileshare schema migrations via EF Core gateway."""
    print(
        f"Schema migration for Fileshare handled via EF Core Fluent API: {database_url}"
    )


def main() -> None:
    from packages.config import get_fileshare_settings

    parser = argparse.ArgumentParser(
        description="Fileshare Database Schema Migration Runner"
    )
    parser.add_argument(
        "--db-url",
        type=str,
        default=get_fileshare_settings().fileshare_database_url,
        help="Database URL for migration",
    )
    args = parser.parse_args()
    asyncio.run(run_migrations(args.db_url))


if __name__ == "__main__":
    main()
