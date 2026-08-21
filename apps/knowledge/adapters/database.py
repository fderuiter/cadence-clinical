"""Database adapter for the Knowledge microservice."""

from packages.database import DatabaseSessionDependency, RelationalDatabaseManager

db_manager = RelationalDatabaseManager("knowledge")
get_db_session = DatabaseSessionDependency(db_manager)
