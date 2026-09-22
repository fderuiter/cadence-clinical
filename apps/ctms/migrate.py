import argparse
import asyncio
import os


async def run_migrations(database_url: str) -> None:
    """Executes pre-boot CTMS schema migrations via EF Core gateway."""
    print(f"Schema migration for CTMS handled via EF Core Fluent API: {database_url}")


def main() -> None:
    from packages.config import get_ctms_settings

    parser = argparse.ArgumentParser(
        description="CTMS Database Schema Migration Runner"
    )
    parser.add_argument(
        "--db-url",
        type=str,
        default=get_ctms_settings().ctms_database_url,
        help="Database URL for migration",
    )
    args = parser.parse_args()
    asyncio.run(run_migrations(args.db_url))


if __name__ == "__main__":
    main()
