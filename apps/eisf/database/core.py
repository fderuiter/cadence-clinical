from packages.database import RelationalDatabaseManager


class EISFDatabaseManager(RelationalDatabaseManager):
    """
    Database manager for the electronic Investigator Site File (eISF) service.
    Configurable via EISF_DATABASE_URL environment variable.
    """

    def __init__(self) -> None:
        super().__init__(service_name="eISF")


# Singleton instance
db_manager = EISFDatabaseManager()
