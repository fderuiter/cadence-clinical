from typing import Any, Optional

from sqlalchemy import text
from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker, create_async_engine


async def setup_database_triggers(conn: Any) -> None:
    """Sets up write-protection triggers on audit tables."""
    dialect = conn.dialect.name
    if dialect == "sqlite":
        # Allow setting block_id when it is NULL, but nothing else.
        sqlite_trigger = """
        CREATE TRIGGER IF NOT EXISTS prevent_audit_log_update 
        BEFORE UPDATE ON audit_logs 
        WHEN OLD.block_id IS NOT NULL OR NEW.block_id IS OLD.block_id
        BEGIN SELECT RAISE(ABORT, 'Updates to audit_logs are disabled'); END;
        """
        await conn.execute(text(sqlite_trigger))
        await conn.execute(
            text(
                "CREATE TRIGGER IF NOT EXISTS prevent_audit_log_delete BEFORE DELETE ON audit_logs BEGIN SELECT RAISE(ABORT, 'Deletes from audit_logs are disabled'); END;"
            )
        )
        await conn.execute(
            text(
                "CREATE TRIGGER IF NOT EXISTS prevent_ledger_update BEFORE UPDATE ON audit_ledger_blocks BEGIN SELECT RAISE(ABORT, 'Updates to audit_ledger_blocks are disabled'); END;"
            )
        )
        await conn.execute(
            text(
                "CREATE TRIGGER IF NOT EXISTS prevent_ledger_delete BEFORE DELETE ON audit_ledger_blocks BEGIN SELECT RAISE(ABORT, 'Deletes from audit_ledger_blocks are disabled'); END;"
            )
        )
    elif dialect == "postgresql":
        func_sql = """
        CREATE OR REPLACE FUNCTION prevent_modifications() RETURNS TRIGGER AS $$
        BEGIN
            IF TG_OP = 'UPDATE' AND TG_TABLE_NAME = 'audit_logs' THEN
                IF OLD.block_id IS NULL AND NEW.block_id IS NOT NULL AND OLD.id = NEW.id AND OLD.action = NEW.action THEN
                    RETURN NEW;
                END IF;
            END IF;
            RAISE EXCEPTION 'Modifications to this table are disabled';
        END;
        $$ LANGUAGE plpgsql;
        """
        await conn.execute(text(func_sql))

        # PostgreSQL doesn't support IF NOT EXISTS on CREATE TRIGGER easily without checking pg_trigger, but we can drop if exists then create.
        trigger_sqls = [
            "DROP TRIGGER IF EXISTS prevent_audit_log_update ON audit_logs",
            "CREATE TRIGGER prevent_audit_log_update BEFORE UPDATE ON audit_logs FOR EACH ROW EXECUTE PROCEDURE prevent_modifications()",
            "DROP TRIGGER IF EXISTS prevent_audit_log_delete ON audit_logs",
            "CREATE TRIGGER prevent_audit_log_delete BEFORE DELETE ON audit_logs FOR EACH ROW EXECUTE PROCEDURE prevent_modifications()",
            "DROP TRIGGER IF EXISTS prevent_ledger_update ON audit_ledger_blocks",
            "CREATE TRIGGER prevent_ledger_update BEFORE UPDATE ON audit_ledger_blocks FOR EACH ROW EXECUTE PROCEDURE prevent_modifications()",
            "DROP TRIGGER IF EXISTS prevent_ledger_delete ON audit_ledger_blocks",
            "CREATE TRIGGER prevent_ledger_delete BEFORE DELETE ON audit_ledger_blocks FOR EACH ROW EXECUTE PROCEDURE prevent_modifications()",
        ]
        for sql in trigger_sqls:
            await conn.execute(text(sql))


class DatabaseSessionManager:
    """
    Manages the lifecycle of database connections and sessions.

    This unified manager simplifies initialization and teardown of the
    asynchronous SQLAlchemy engine and session makers, facilitating
    both application runtime execution and test configurations.
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
        self.engine = create_async_engine(database_url, **kwargs)
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


db_manager: DatabaseSessionManager = DatabaseSessionManager()
