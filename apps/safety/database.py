from packages.database import RelationalDatabaseManager


class SafetyDatabaseManager(RelationalDatabaseManager):
    pass


db_manager = SafetyDatabaseManager(service_name="Safety")
