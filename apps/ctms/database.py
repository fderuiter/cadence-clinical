from packages.database import RelationalDatabaseManager


class CTMSDatabaseManager(RelationalDatabaseManager):
    """
    Manages the lifecycle of the CTMS service's database connections and sessions.
    """

    def __init__(self) -> None:
        super().__init__(service_name="CTMS")


db_manager = CTMSDatabaseManager()
