from packages.database import DatabaseSessionManager

# For backward compatibility if needed
InteropDatabaseManager = DatabaseSessionManager

db_manager: DatabaseSessionManager = DatabaseSessionManager()
