#!/usr/bin/env python3
"""
Programmatic Multi-Database Reset Tool (Wrapper for db_lifecycle.py).

Wipes, resets, migrates, and seeds PostgreSQL, Neo4j, and SQLite instances.
Delegates to scripts/db_lifecycle.py.
"""

import asyncio

from scripts.db_lifecycle import main

if __name__ == "__main__":
    asyncio.run(main())
