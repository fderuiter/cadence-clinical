from packages.database import RelationalDatabaseManager


class SafetyDatabaseManager(RelationalDatabaseManager):
    """
    Service-local database manager for the Safety microservice,
    inheriting from RelationalDatabaseManager to follow consistent GxP architecture.
    """

    pass


db_manager = SafetyDatabaseManager(service_name="Safety")
