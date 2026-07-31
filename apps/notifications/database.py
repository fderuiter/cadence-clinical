from packages.database import RelationalDatabaseManager


class NotificationsDatabaseManager(RelationalDatabaseManager):
    """
    Service-specific relational database manager for the Notifications microservice.
    """

    def __init__(self, service_name: str = "Notifications") -> None:
        super().__init__(service_name=service_name)


db_manager = NotificationsDatabaseManager()
