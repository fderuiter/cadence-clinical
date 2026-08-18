import asyncio
import uuid
from typing import Any

from sqlalchemy import event
from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker, create_async_engine


class DatabaseSessionManager:
    """
    Manages the lifecycle of database connections and sessions.

    This unified manager simplifies initialization and teardown of the
    asynchronous SQLAlchemy engine and session makers, facilitating
    both application runtime execution and test configurations.
    """

    def __init__(self) -> None:
        """Initialize the DatabaseSessionManager with empty state.

        Attributes:
            engine (Any): The database engine instance.
            session_maker (async_sessionmaker[AsyncSession] | None): Factory for producing sessions.
            _sqlite_settings (dict[int, dict[str, str | None]]): Maps connection IDs to their SQLite configurations.
        """
        self.engine: Any = None
        self.session_maker: async_sessionmaker[AsyncSession] | None = None
        self._sqlite_settings: dict[int, dict[str, str | None]] = {}

    def init_db(self, database_url: str, **kwargs: Any) -> None:
        """Initialize the database engine and session maker.

        Args:
            database_url (str): The connection string for the database.
            **kwargs (Any): Additional arguments to pass to the async engine.
        """
        self._sqlite_settings.clear()
        engine_options = {}
        if database_url.startswith("sqlite"):
            engine_options["execution_options"] = {
                "schema_translate_map": {"audit_schema": None}
            }

        self.engine = create_async_engine(database_url, **{**engine_options, **kwargs})

        def _get_raw_connection(dbapi_connection: Any) -> Any:
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
            return conn

        @event.listens_for(self.engine.sync_engine, "connect")
        def set_sqlite_pragma(dbapi_connection, connection_record):
            conn = _get_raw_connection(dbapi_connection)

            if hasattr(conn, "create_function"):
                conn_id = id(conn)
                if conn_id not in self._sqlite_settings:
                    self._sqlite_settings[conn_id] = {
                        "cadence.current_user_id": "system",
                        "cadence.current_change_reason": "system_operation",
                        "cadence.app_writing": "false",
                    }

                def sqlite_set_config(name, value, is_local=True):
                    if conn_id not in self._sqlite_settings:
                        self._sqlite_settings[conn_id] = {}
                    self._sqlite_settings[conn_id][name] = (
                        str(value) if value is not None else None
                    )
                    return value

                def sqlite_current_setting(name, missing_ok=True):
                    if conn_id not in self._sqlite_settings:
                        return ""
                    val = self._sqlite_settings[conn_id].get(name)
                    if val is None:
                        if missing_ok:
                            return ""
                        raise Exception(f"Setting {name} not found")
                    return val

                conn.create_function("set_config", 3, sqlite_set_config)
                conn.create_function("current_setting", 2, sqlite_current_setting)
                conn.create_function("gen_random_uuid", 0, lambda: str(uuid.uuid4()))

        @event.listens_for(self.engine.sync_engine, "checkout")
        def reset_sqlite_on_checkout(
            dbapi_connection, connection_record, connection_proxy
        ):
            conn = _get_raw_connection(dbapi_connection)
            if hasattr(conn, "create_function"):
                conn_id = id(conn)
                self._sqlite_settings[conn_id] = {
                    "cadence.current_user_id": "system",
                    "cadence.current_change_reason": "system_operation",
                    "cadence.app_writing": "false",
                }

        @event.listens_for(self.engine.sync_engine, "checkin")
        def reset_sqlite_on_checkin(dbapi_connection, connection_record):
            conn = _get_raw_connection(dbapi_connection)
            if hasattr(conn, "create_function"):
                conn_id = id(conn)
                self._sqlite_settings[conn_id] = {
                    "cadence.current_user_id": "system",
                    "cadence.current_change_reason": "system_operation",
                    "cadence.app_writing": "false",
                }

        @event.listens_for(self.engine.sync_engine, "close")
        def evict_sqlite_on_close(dbapi_connection, connection_record):
            conn = _get_raw_connection(dbapi_connection)
            if hasattr(conn, "create_function"):
                conn_id = id(conn)
                self._sqlite_settings.pop(conn_id, None)

        @event.listens_for(self.engine.sync_engine, "close_detached")
        def evict_sqlite_on_close_detached(dbapi_connection):
            conn = _get_raw_connection(dbapi_connection)
            if hasattr(conn, "create_function"):
                conn_id = id(conn)
                self._sqlite_settings.pop(conn_id, None)

        self.session_maker = async_sessionmaker(
            bind=self.engine, class_=AsyncSession, expire_on_commit=False
        )

    async def close(self) -> None:
        """Close the database engine and clear the session maker."""
        if self.engine:
            await self.engine.dispose()
            self.engine = None
            self.session_maker = None
        self._sqlite_settings.clear()
        await asyncio.sleep(0)

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


db_manager: DatabaseSessionManager = DatabaseSessionManager()
bg_db_manager: DatabaseSessionManager = DatabaseSessionManager()
