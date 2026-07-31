from packages.database import RelationalDatabaseManager

class NotificationsDatabaseManager(RelationalDatabaseManager):
    def __init__(self) -> None:
        super().__init__(service_name="Notifications")

db_manager = NotificationsDatabaseManager()
