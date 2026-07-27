from packages.database import RelationalDatabaseManager


class InteropDatabaseManager(RelationalDatabaseManager):
    """
    Manages the lifecycle of the Interop service's database connections and sessions.
    """

    def __init__(self) -> None:
        super().__init__(service_name="Interop")


db_manager = InteropDatabaseManager()
