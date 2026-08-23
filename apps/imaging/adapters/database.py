"""Database adapter for the Medical Imaging microservice."""

from packages.database import DatabaseSessionDependency, RelationalDatabaseManager

db_manager = RelationalDatabaseManager("imaging")
get_db_session = DatabaseSessionDependency(db_manager)
