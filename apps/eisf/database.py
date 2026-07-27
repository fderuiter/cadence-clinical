import os
from typing import Any, Optional

from packages.database import RelationalDatabaseManager


class EISFDatabaseManager(RelationalDatabaseManager):
    """
    Manages the lifecycle of the eISF service's database connections and sessions.
    """

    def __init__(self) -> None:
        super().__init__(service_name="eISF")

    def init_db(self, database_url: Optional[str] = None, **kwargs: Any) -> None:
        """
        Initialize the async engine and session maker for the eISF database.
        Allows override via EISF_DATABASE_URL.
        """
        if not database_url:
            database_url = os.environ.get(
                "EISF_DATABASE_URL", "sqlite+aiosqlite:///:memory:"
            )
        super().init_db(database_url, **kwargs)


db_manager = EISFDatabaseManager()
