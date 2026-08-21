"""Database schema migration runner for the Fileshare microservice."""

import argparse
import asyncio
import os
import sys


async def run_migrations(database_url: str) -> None:
    """Executes pre-boot Fileshare schema migrations."""
    print(f"Starting pre-boot schema migration for Fileshare: {database_url}")
    env = os.environ.copy()
    env["FILESHARE_DATABASE_URL"] = database_url
    cmd = [
        sys.executable,
        "-m",
        "alembic",
        "-c",
        "apps/fileshare/alembic.ini",
        "upgrade",
        "head",
    ]
    process = await asyncio.create_subprocess_exec(
        *cmd, env=env, stdout=asyncio.subprocess.PIPE, stderr=asyncio.subprocess.PIPE
    )
    stdout, stderr = await process.communicate()
    if process.returncode != 0:
        print(f"Alembic migration failed: {stderr.decode()}", file=sys.stderr)
        raise RuntimeError(f"Alembic migration failed for Fileshare: {stderr.decode()}")
    print("Fileshare Schema migration completed successfully via Alembic.")


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Fileshare Database Schema Migration Runner"
    )
    parser.add_argument(
        "--db-url",
        type=str,
        default=os.getenv(
            "FILESHARE_DATABASE_URL", "sqlite+aiosqlite:///:memory:"
        ),
        help="Database URL for migration",
    )
    args = parser.parse_args()
    asyncio.run(run_migrations(args.db_url))


if __name__ == "__main__":
    main()

