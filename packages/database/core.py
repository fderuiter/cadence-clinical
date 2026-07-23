import uuid
from typing import Any, Optional

from sqlalchemy import event
from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker, create_async_engine

# Global connection-mapped settings dictionary.
# Maps the ID of a low-level SQLite DBAPI connection to its setting values.
# This dictionary is updated by the main-thread `before_cursor_execute` hook
# and read by connection-specific SQLite custom function closures.
_sqlite_connection_settings = {}


class DatabaseSessionManager:
    """
    Manages the lifecycle of database connections and sessions.

    This unified manager simplifies initialization and teardown of the
    asynchronous SQLAlchemy engine and session makers, facilitating
    both application runtime execution and test configurations across all microservices.
    """

    def __init__(self) -> None:
        """Initialize the DatabaseSessionManager with empty state."""
        self.engine: Any = None
        self.session_maker: Optional[async_sessionmaker[AsyncSession]] = None

    def init_db(self, database_url: str, **kwargs: Any) -> None:
        """
        Initialize the database engine and session maker.

        Args:
            database_url (str): The connection string for the database.
            **kwargs (Any): Additional arguments to pass to the async engine.
        """
        engine_options = {}
        if database_url.startswith("sqlite"):
            engine_options["execution_options"] = {
                "schema_translate_map": {"audit_schema": None}
            }

        self.engine = create_async_engine(database_url, **{**engine_options, **kwargs})

        @event.listens_for(self.engine.sync_engine, "connect")
        def configure_sqlite_connection(dbapi_connection, connection_record):
            # Globally enforce relational integrity constraints (Foreign Keys) on all SQLite connections
            cursor = dbapi_connection.cursor()
            try:
                cursor.execute("PRAGMA foreign_keys=ON")
            except Exception:
                pass
            finally:
                cursor.close()

            # Register PostgreSQL compatibility functions for SQLite compatibility
            conn = dbapi_connection
            for _ in range(5):
                if hasattr(conn, "create_function"):
                    break
                if hasattr(conn, "connection"):
                    conn = conn.connection
                elif hasattr(conn, "_connection"):
                    conn = conn._connection
                elif hasattr(conn, "dbapi_connection"):
                    conn = conn.dbapi_connection
                else:
                    break

            if hasattr(conn, "create_function"):
                conn_id = id(conn)
                if conn_id not in _sqlite_connection_settings:
                    _sqlite_connection_settings[conn_id] = {
                        "cadence.current_user_id": "system",
                        "cadence.current_change_reason": "system_operation",
                        "cadence.app_writing": "false",
                    }

                def sqlite_set_config(name: str, value: Any, is_local: bool = True) -> Any:
                    if conn_id not in _sqlite_connection_settings:
                        _sqlite_connection_settings[conn_id] = {}
                    _sqlite_connection_settings[conn_id][name] = (
                        str(value) if value is not None else None
                    )
                    return value

                def sqlite_current_setting(name: str, missing_ok: Any = True) -> str:
                    missing_ok_bool = (
                        bool(missing_ok) if isinstance(missing_ok, int) else missing_ok
                    )
                    if conn_id not in _sqlite_connection_settings:
                        return ""
                    val = _sqlite_connection_settings[conn_id].get(name)
                    if val is None:
                        if missing_ok_bool:
                            return ""
                        else:
                            raise Exception(f"Setting {name} not found")
                    return val

                conn.create_function("set_config", 3, sqlite_set_config)
                conn.create_function("current_setting", 1, sqlite_current_setting)
                conn.create_function("current_setting", 2, sqlite_current_setting)
                conn.create_function("gen_random_uuid", 0, lambda: str(uuid.uuid4()))

        # Listen to before_cursor_execute on the main thread to sync ContextVars to connection-specific settings
        @event.listens_for(self.engine.sync_engine, "before_cursor_execute")
        def sync_context_variables(conn, cursor, statement, parameters, context, executemany):
            dbapi_conn = conn.connection.dbapi_connection if hasattr(conn.connection, "dbapi_connection") else conn.connection
            for _ in range(5):
                if hasattr(dbapi_conn, "connection"):
                    dbapi_conn = dbapi_conn.connection
                elif hasattr(dbapi_conn, "_connection"):
                    dbapi_conn = dbapi_conn._connection
                elif hasattr(dbapi_conn, "dbapi_connection"):
                    dbapi_conn = dbapi_conn.dbapi_connection
                else:
                    break

            conn_id = id(dbapi_conn)
            if conn_id not in _sqlite_connection_settings:
                _sqlite_connection_settings[conn_id] = {
                    "cadence.current_user_id": "system",
                    "cadence.current_change_reason": "system_operation",
                    "cadence.app_writing": "false",
                }

            # Safely sync active security ContextVars to connection-level dictionary on the main thread
            try:
                from packages.security.context import current_user_id
                val = current_user_id.get()
                if val is not None:
                    _sqlite_connection_settings[conn_id]["cadence.current_user_id"] = str(val)
            except Exception:
                pass

            try:
                from packages.security.context import current_change_reason
                val = current_change_reason.get()
                if val is not None:
                    _sqlite_connection_settings[conn_id]["cadence.current_change_reason"] = str(val)
            except Exception:
                pass

        self.session_maker = async_sessionmaker(
            bind=self.engine, class_=AsyncSession, expire_on_commit=False
        )

    async def close(self) -> None:
        """Close the database engine and clear the session maker."""
        if self.engine:
            await self.engine.dispose()
            self.engine = None
            self.session_maker = None

    def get_session_maker(self) -> async_sessionmaker[AsyncSession]:
        """
        Retrieve the configured async session maker.

        Returns:
            async_sessionmaker[AsyncSession]: The active session factory.

        Raises:
            Exception: If the database has not been initialized.
        """
        if not self.session_maker:
            raise Exception("Database session manager is not initialized.")
        return self.session_maker
