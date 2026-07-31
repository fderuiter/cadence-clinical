import os
from packages.database import RelationalDatabaseManager

class EISFDatabaseManager(RelationalDatabaseManager):
    """
    Database manager for the eISF service.
    """
    def __init__(self) -> None:
        super().__init__(service_name="eISF")

# EISF_DATABASE_URL env var defaulting to in-memory SQLite for tests
EISF_DATABASE_URL = os.getenv("EISF_DATABASE_URL", "sqlite+aiosqlite:///:memory:")

db_manager = EISFDatabaseManager()
