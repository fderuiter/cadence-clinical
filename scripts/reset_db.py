#!/usr/bin/env python3
"""
Programmatic Multi-Database CLI Tool (Legacy Entrypoint).

Delegates multi-database reset, migration, and seeding operations to scripts/db_lifecycle.py.
"""

import asyncio

from scripts.db_lifecycle import (
    main,
    reset_neo4j,
    reset_postgres,
    reset_sqlite_db,
    seed_sqlite_edl,
    validate_local_only,
)

__all__ = [
    "main",
    "reset_neo4j",
    "reset_postgres",
    "reset_sqlite_db",
    "seed_sqlite_edl",
    "validate_local_only",
]

if __name__ == "__main__":
    asyncio.run(main())
