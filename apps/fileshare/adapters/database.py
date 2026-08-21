"""Database adapter for the Fileshare microservice."""

from packages.database import DatabaseSessionDependency, RelationalDatabaseManager

db_manager = RelationalDatabaseManager("fileshare")
get_db_session = DatabaseSessionDependency(db_manager)

