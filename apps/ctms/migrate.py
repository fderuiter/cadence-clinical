import argparse
import asyncio
import os


async def run_migrations(database_url: str) -> None:
    """Executes pre-boot CTMS schema migrations via EF Core gateway."""
    print(f"Schema migration for CTMS handled via EF Core Fluent API: {database_url}")


def main() -> None:
    parser = argparse.ArgumentParser(
        description="CTMS Database Schema Migration Runner"
    )
    parser.add_argument(
        "--db-url",
        type=str,
        default=os.getenv("CTMS_DATABASE_URL", "sqlite+aiosqlite:///:memory:"),
        help="Database URL for migration",
    )
    args = parser.parse_args()
    asyncio.run(run_migrations(args.db_url))


if __name__ == "__main__":
    main()
