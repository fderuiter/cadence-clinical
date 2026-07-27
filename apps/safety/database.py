from packages.database import RelationalDatabaseManager


class SafetyDatabaseManager(RelationalDatabaseManager):
    """
    Manages the lifecycle of the Safety service's database connections and sessions.
    """

    def __init__(self) -> None:
        super().__init__(service_name="Safety")


db_manager = SafetyDatabaseManager()
