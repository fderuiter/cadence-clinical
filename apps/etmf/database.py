from packages.database import DatabaseSessionManager

# For backward compatibility if needed
ETMFDatabaseManager = DatabaseSessionManager

db_manager: DatabaseSessionManager = DatabaseSessionManager()
