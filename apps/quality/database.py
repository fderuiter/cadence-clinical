from packages.database import RelationalDatabaseManager


class QualityDatabaseManager(RelationalDatabaseManager):
    """
    Manages the lifecycle of the Quality & CAPA service's database connections and sessions.
    """

    def __init__(self) -> None:
        super().__init__(service_name="Quality")


db_manager = QualityDatabaseManager()
