"""
Database session management for the Organization Directory microservice.

Integrates with the shared parameterized RelationalDatabaseManager to support
SQLite and future PostgreSQL configurations under async SQLAlchemy.
"""

from packages.database import RelationalDatabaseManager


class OrgDatabaseManager(RelationalDatabaseManager):
    """
    Manages the lifecycle of the Organization Directory database connections and sessions.
    """

    def __init__(self) -> None:
        super().__init__(service_name="Org")


db_manager = OrgDatabaseManager()
