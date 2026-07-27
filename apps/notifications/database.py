from packages.database import RelationalDatabaseManager


class NotificationsDatabaseManager(RelationalDatabaseManager):
    """
    Manages the lifecycle of the Notifications service's database connections and sessions.
    """

    def __init__(self) -> None:
        super().__init__(service_name="Notifications")


db_manager = NotificationsDatabaseManager()
