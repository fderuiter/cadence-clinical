"""
Database manager configuration for the Tickets service.
"""

from packages.database import DatabaseSessionDependency, RelationalDatabaseManager

db_manager = RelationalDatabaseManager(service_name="Tickets")
get_db_session = DatabaseSessionDependency(db_manager)
