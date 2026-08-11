import uuid
from typing import Any

from sqlalchemy import event
from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker

from packages.database import RelationalDatabaseManager


class DatabaseSessionManager(RelationalDatabaseManager):
    """
    Manages the lifecycle of database connections and sessions.

    This unified manager simplifies initialization and teardown of the
    asynchronous SQLAlchemy engine and session makers, facilitating
    both application runtime execution and test configurations.
    """

    def __init__(self) -> None:
        """Initialize the DatabaseSessionManager with empty state."""
        super().__init__("execution")

    def init_db(self, database_url: str, **kwargs: Any) -> None:
        """
        Initialize the database engine and session maker.

        Args:
            database_url (str): The connection string for the database.
            **kwargs (Any): Additional arguments to pass to the async engine.
        """
        if database_url.startswith("sqlite"):
            execution_options = kwargs.get("execution_options", {})
            schema_translate_map = execution_options.get("schema_translate_map", {})
            schema_translate_map["audit_schema"] = None
            execution_options["schema_translate_map"] = schema_translate_map
            kwargs["execution_options"] = execution_options

        super().init_db(database_url, **kwargs)

        if database_url.startswith("sqlite"):
            _sqlite_settings = {}

            @event.listens_for(self.engine.sync_engine, "connect")
            def set_sqlite_pragma_and_funcs(dbapi_connection, connection_record):
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
                    if conn_id not in _sqlite_settings:
                        _sqlite_settings[conn_id] = {
                            "cadence.current_user_id": "system",
                            "cadence.current_change_reason": "system_operation",
                            "cadence.app_writing": "false",
                        }

                    def sqlite_set_config(name, value, is_local=True):
                        if conn_id not in _sqlite_settings:
                            _sqlite_settings[conn_id] = {}
                        _sqlite_settings[conn_id][name] = (
                            str(value) if value is not None else None
                        )
                        return value

                    def sqlite_current_setting(name, missing_ok=True):
                        if conn_id not in _sqlite_settings:
                            return ""
                        val = _sqlite_settings[conn_id].get(name)
                        if val is None:
                            if missing_ok:
                                return ""
                            raise Exception(f"Setting {name} not found")
                        return val

                    conn.create_function("set_config", 3, sqlite_set_config)
                    conn.create_function("current_setting", 2, sqlite_current_setting)
                    conn.create_function(
                        "gen_random_uuid", 0, lambda: str(uuid.uuid4())
                    )

    async def close(self) -> None:
        """Close the database engine and clear the session maker."""
        await super().close()

    def get_session_maker(self) -> async_sessionmaker[AsyncSession]:
        """
        Retrieve the configured async session maker.

        Returns:
            async_sessionmaker[AsyncSession]: The active session factory.

        Raises:
            Exception: If the database has not been initialized.
        """
        return super().get_session_maker()


db_manager: DatabaseSessionManager = DatabaseSessionManager()
