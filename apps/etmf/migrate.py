"""
eTMF Database Migration and Backfill Module delegation.
"""

from apps.etmf.database.migrate import main as main
from apps.etmf.database.migrate import run_migrations as run_migrations
from apps.etmf.database.migrate import (
    upgrade_existing_tables as upgrade_existing_tables,
)

__all__ = ["main", "run_migrations", "upgrade_existing_tables"]

if __name__ == "__main__":
    main()
