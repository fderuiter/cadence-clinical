#!/usr/bin/env python3
"""Clean Test Databases Utility — Cadence Clinical.

Safely inspects and drops orphaned worker-isolated PostgreSQL test databases
from current or previous pytest / xdist test runs.

Usage:
    uv run python scripts/clean_test_dbs.py --list
    uv run python scripts/clean_test_dbs.py --all
    uv run python scripts/clean_test_dbs.py --run-id <8-char-hex>
    uv run python scripts/clean_test_dbs.py --all --dry-run
"""

from __future__ import annotations

import argparse
import asyncio
import os
import re
import sys

import asyncpg


def get_postgres_base_url() -> str:
    """Resolve base PostgreSQL connection URL."""
    url = (
        os.environ.get("TEST_DATABASE_URL")
        or os.environ.get("DATABASE_URL")
        or "postgresql://cadence:cadence_password@localhost:5432/postgres"  # pragma: allowlist secret
    )
    clean_url = url.replace("postgresql+asyncpg://", "postgresql://")
    if "://" in clean_url:
        scheme, remainder = clean_url.split("://", 1)
        if "/" in remainder:
            base_part, _ = remainder.rsplit("/", 1)
        else:
            base_part = remainder
        if "@" not in base_part:
            base_part = f"cadence:cadence_password@{base_part}"
        return f"{scheme}://{base_part}/postgres"
    return "postgresql://cadence:cadence_password@localhost:5432/postgres"  # pragma: allowlist secret


# Standard Cadence test DB pattern: cadence_<service>_<run_uid>_<worker>
TEST_DB_PATTERN = re.compile(
    r"^cadence_(edc|etmf|ctms|quality|interop|tickets|notifications|econsent|safety|org|eisf)_[0-9a-zA-Z]+_(gw\d+|main)$"
)


async def find_test_databases(
    conn: asyncpg.Connection, run_id: str | None = None
) -> list[str]:
    """Find all test databases, optionally filtered by run ID."""
    rows = await conn.fetch(
        "SELECT datname FROM pg_database WHERE datname LIKE 'cadence_%' ORDER BY datname"
    )
    found = []
    for row in rows:
        db_name = row["datname"]
        if TEST_DB_PATTERN.match(db_name) or "_gw" in db_name or "_main" in db_name:
            if run_id:
                if f"_{run_id}_" in db_name:
                    found.append(db_name)
            else:
                found.append(db_name)
    return found


async def drop_databases(
    conn: asyncpg.Connection, db_names: list[str], dry_run: bool = False
) -> int:
    """Drop specified databases after terminating any active connections."""
    dropped_count = 0
    for db_name in db_names:
        if dry_run:
            print(f"[dry-run] Would drop: {db_name}")
            dropped_count += 1
            continue

        try:
            await conn.execute(f"""
                SELECT pg_terminate_backend(pid)
                FROM pg_stat_activity
                WHERE datname = '{db_name}'
                  AND pid <> pg_backend_pid();
            """)
            await conn.execute(f"DROP DATABASE IF EXISTS {db_name};")
            print(f"✔ Dropped test database: {db_name}")
            dropped_count += 1
        except Exception as e:
            print(f"✘ Error dropping {db_name}: {e}", file=sys.stderr)

    return dropped_count


async def main_async(args: argparse.Namespace) -> int:
    pg_url = get_postgres_base_url()
    try:
        conn = await asyncpg.connect(pg_url, timeout=5.0)
    except Exception as e:
        print(f"✘ Failed to connect to PostgreSQL at {pg_url}: {e}", file=sys.stderr)
        return 1

    try:
        dbs = await find_test_databases(conn, run_id=args.run_id)
        if not dbs:
            target = f" matching run ID '{args.run_id}'" if args.run_id else ""
            print(f"No test databases found{target}.")
            return 0

        print(f"Found {len(dbs)} test database(s):")
        for db in dbs:
            print(f"  - {db}")

        if args.list:
            return 0

        if not args.all and not args.run_id:
            print("\nSpecify --all or --run-id <id> to drop these databases.")
            return 0

        dropped = await drop_databases(conn, dbs, dry_run=args.dry_run)
        action = "Would drop" if args.dry_run else "Successfully dropped"
        print(f"\n{action} {dropped} database(s).")
        return 0
    finally:
        await conn.close()


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Inspect and clean worker-isolated test databases in PostgreSQL."
    )
    parser.add_argument(
        "--list",
        "-l",
        action="store_true",
        help="List detected test databases without dropping them.",
    )
    parser.add_argument(
        "--all",
        "-a",
        action="store_true",
        help="Drop all detected test databases.",
    )
    parser.add_argument(
        "--run-id",
        "-r",
        type=str,
        help="Drop only test databases matching a specific run UID.",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Preview actions without executing DROP DATABASE commands.",
    )
    args = parser.parse_args()

    sys.exit(asyncio.run(main_async(args)))


if __name__ == "__main__":
    main()
