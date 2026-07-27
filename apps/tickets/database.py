"""
Database manager configuration for the Tickets service.
"""

from packages.database import RelationalDatabaseManager

db_manager = RelationalDatabaseManager(service_name="Tickets")
